using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Domain.Exceptions;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

public class DiscountService : IDiscountService
{
    private readonly IDiscountRepository _discountRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly ILogger<DiscountService> _logger;

    public DiscountService(
        IDiscountRepository discountRepo,
        ICustomerRepository customerRepo,
        ILogger<DiscountService> logger)
    {
        _discountRepo = discountRepo;
        _customerRepo = customerRepo;
        _logger = logger;
    }

    public async Task<Discount?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        return await _discountRepo.GetByCodeAsync(code.ToUpperInvariant().Trim(), ct);
    }

    public async Task<IReadOnlyList<Discount>> GetActiveAsync(CancellationToken ct = default)
    {
        return await _discountRepo.GetActiveAsync(ct);
    }

    public async Task<Discount> CreateAsync(Discount discount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(discount.Code))
            throw new DiscountException("Discount code cannot be empty.");

        discount.Code = discount.Code.ToUpperInvariant().Trim();

        var existing = await _discountRepo.GetByCodeAsync(discount.Code, ct);
        if (existing != null)
            throw new DiscountException($"Discount code '{discount.Code}' already exists.");

        discount.CreatedAt = DateTime.UtcNow;
        discount.Status    = DiscountStatus.Draft;

        return await _discountRepo.CreateAsync(discount, ct);
    }

    public async Task<Discount> UpdateAsync(Discount discount, CancellationToken ct = default)
    {
        var existing = await _discountRepo.GetByIdAsync(discount.Id, ct)
            ?? throw new DiscountException($"Discount {discount.Id} not found.");

        if (existing.Status == DiscountStatus.Depleted)
            throw new DiscountException("Cannot edit a depleted discount.");

        existing.Name             = discount.Name;
        existing.Description      = discount.Description;
        existing.Value            = discount.Value;
        existing.MaxDiscountAmount = discount.MaxDiscountAmount;
        existing.MinimumOrderAmount = discount.MinimumOrderAmount;
        existing.ExpiresAt        = discount.ExpiresAt;
        existing.UsageLimitTotal  = discount.UsageLimitTotal;
        existing.IsStackable      = discount.IsStackable;
        existing.Status           = discount.Status;
        existing.UpdatedAt        = DateTime.UtcNow;

        return await _discountRepo.UpdateAsync(existing, ct);
    }

    public async Task<bool> ValidateAsync(
        string code, int customerId, decimal orderTotal, CancellationToken ct = default)
    {
        var discount = await _discountRepo.GetByCodeAsync(code.ToUpperInvariant(), ct);
        if (discount == null || !discount.IsActive) return false;

        if (discount.MinimumOrderAmount.HasValue && orderTotal < discount.MinimumOrderAmount)
            return false;

        if (discount.UsageLimitPerCustomer.HasValue)
        {
            var usageCount = await _discountRepo.GetUsageCountByCustomerAsync(
                discount.Id, customerId, ct);
            if (usageCount >= discount.UsageLimitPerCustomer)
                return false;
        }

        if (discount.IsFirstOrderOnly)
        {
            var customerOrders = await _customerRepo.GetOrderCountAsync(customerId, ct);
            if (customerOrders > 0) return false;
        }

        return true;
    }

    // NOVA-58: When multiple stackable discounts are provided, the
    // ConflictResolution strategy is read but never used. The loop always
    // applies every stackable discount additively regardless of the strategy.
    // E.g. SUMMER20 (20%) + GOLD10 (10%) both apply, giving 30% off instead
    // of the intended 20% (UseHighest). Also: MaxDiscountAmount cap is checked
    // per-discount, not against the running total — so caps are bypassed.
    public async Task<List<DiscountApplication>> ApplyDiscountsAsync(
        IEnumerable<string> codes,
        int customerId,
        decimal orderTotal,
        CustomerTier customerTier,
        CancellationToken ct = default)
    {
        var results   = new List<DiscountApplication>();
        var discounts = new List<Discount>();

        foreach (var code in codes)
        {
            var d = await _discountRepo.GetByCodeAsync(code.ToUpperInvariant(), ct);
            if (d != null && d.IsActive)
                discounts.Add(d);
        }

        // auto-apply tier discount if customer qualifies — added in v2.1
        var tierDiscount = await _discountRepo.GetTierDiscountAsync(customerTier, ct);
        if (tierDiscount != null && tierDiscount.IsActive)
            discounts.Add(tierDiscount);

        if (!discounts.Any())
            return results;

        // separate stackable from non-stackable
        var stackable    = discounts.Where(d => d.IsStackable).ToList();
        var nonStackable = discounts.Where(d => !d.IsStackable).ToList();

        // pick one non-stackable: should use ConflictResolution but we just take first
        Discount? chosenNonStackable = nonStackable.FirstOrDefault();

        // apply the single non-stackable
        if (chosenNonStackable != null)
        {
            var amount = CalculateDiscount(chosenNonStackable, orderTotal);
            results.Add(new DiscountApplication
            {
                DiscountId     = chosenNonStackable.Id,
                DiscountCode   = chosenNonStackable.Code,
                DiscountName   = chosenNonStackable.Name,
                DiscountAmount = amount,
                Type           = chosenNonStackable.Type,
                WasApplied     = true
            });
        }

        // BUG: stackable discounts are ALWAYS applied additively.
        // ConflictResolution on each discount is read but ignored here.
        // All stackable discounts are applied regardless of resolution strategy.
        decimal runningTotal = orderTotal;
        foreach (var d in stackable)
        {
            // should check d.ConflictResolution here — but we don't
            var amount = CalculateDiscount(d, runningTotal);

            // MaxDiscountAmount is checked per-discount, not cumulatively
            if (d.MaxDiscountAmount.HasValue && amount > d.MaxDiscountAmount)
                amount = d.MaxDiscountAmount.Value;

            results.Add(new DiscountApplication
            {
                DiscountId     = d.Id,
                DiscountCode   = d.Code,
                DiscountName   = d.Name,
                DiscountAmount = amount,
                Type           = d.Type,
                WasApplied     = true
            });

            runningTotal -= amount;
        }

        foreach (var skipped in nonStackable.Skip(1))
        {
            results.Add(new DiscountApplication
            {
                DiscountId       = skipped.Id,
                DiscountCode     = skipped.Code,
                DiscountName     = skipped.Name,
                DiscountAmount   = 0,
                Type             = skipped.Type,
                WasApplied       = false,
                RejectionReason  = "Superseded by another discount"
            });
        }

        _logger.LogInformation(
            "Applied {Count} discounts to order (customer {CustomerId}, total {Total:C}): {Codes}",
            results.Count(r => r.WasApplied), customerId, orderTotal,
            string.Join(", ", results.Where(r => r.WasApplied).Select(r => r.DiscountCode)));

        return results;
    }

    public async Task RecordUsageAsync(
        Guid discountId, int customerId, Guid orderId, decimal amountDiscounted,
        CancellationToken ct = default)
    {
        var discount = await _discountRepo.GetByIdAsync(discountId, ct);
        if (discount == null) return;

        discount.UsageCount++;

        if (discount.UsageLimitTotal.HasValue && discount.UsageCount >= discount.UsageLimitTotal)
            discount.Status = DiscountStatus.Depleted;

        await _discountRepo.UpdateAsync(discount, ct);
        await _discountRepo.AddUsageRecordAsync(new DiscountUsageRecord
        {
            DiscountId       = discountId,
            CustomerId       = customerId,
            OrderId          = orderId,
            AmountDiscounted = amountDiscounted,
            UsedAt           = DateTime.UtcNow
        }, ct);
    }

    private static decimal CalculateDiscount(Discount discount, decimal orderTotal) =>
        discount.Type switch
        {
            DiscountType.PercentageOff     => Math.Round(orderTotal * discount.Value / 100, 2),
            DiscountType.FixedAmountOff    => Math.Min(discount.Value, orderTotal),
            DiscountType.TieredPercentage  => CalculateTiered(discount, orderTotal),
            _                              => 0m
        };

    private static decimal CalculateTiered(Discount discount, decimal orderTotal)
    {
        var rule = discount.TieredRules
            .Where(r => orderTotal >= r.MinOrderAmount
                     && (r.MaxOrderAmount == null || orderTotal <= r.MaxOrderAmount))
            .OrderByDescending(r => r.MinOrderAmount)
            .FirstOrDefault();

        return rule == null ? 0m : Math.Round(orderTotal * rule.DiscountPercent / 100, 2);
    }
}
