using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class DiscountRepository : IDiscountRepository
{
    private readonly NovaTechDbContext _db;

    public DiscountRepository(NovaTechDbContext db) => _db = db;

    public async Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Discounts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Discount?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Discounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Code == code &&
                     d.IsActive &&
                     (d.StartsAt == null || d.StartsAt <= now) &&
                     (d.ExpiresAt == null || d.ExpiresAt > now),
                ct);
    }

    public async Task<IReadOnlyList<Discount>> GetActiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Discounts
            .AsNoTracking()
            .Where(d => d.IsActive &&
                        (d.StartsAt == null || d.StartsAt <= now) &&
                        (d.ExpiresAt == null || d.ExpiresAt > now))
            .OrderByDescending(d => d.Priority)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Discount>> GetByCustomerTierAsync(
        CustomerTier tier, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Discounts
            .AsNoTracking()
            .Where(d => d.IsActive &&
                        d.EligibleTiers != null &&
                        d.EligibleTiers.Contains(tier.ToString()) &&
                        (d.ExpiresAt == null || d.ExpiresAt > now))
            .OrderByDescending(d => d.Priority)
            .ToListAsync(ct);
    }

    public async Task<Discount> CreateAsync(Discount discount, CancellationToken ct = default)
    {
        discount.CreatedAt = DateTime.UtcNow;
        _db.Discounts.Add(discount);
        await _db.SaveChangesAsync(ct);
        return discount;
    }

    public async Task<Discount> UpdateAsync(Discount discount, CancellationToken ct = default)
    {
        discount.UpdatedAt = DateTime.UtcNow;
        _db.Discounts.Update(discount);
        await _db.SaveChangesAsync(ct);
        return discount;
    }

    // atomic increment to avoid lost-update on concurrent orders applying the same discount
    public async Task<bool> IncrementUsageAsync(Guid discountId, CancellationToken ct = default)
    {
        var updated = await _db.Discounts
            .Where(d => d.Id == discountId &&
                        (d.UsageLimit == null || d.UsageCount < d.UsageLimit))
            .ExecuteUpdateAsync(
                s => s.SetProperty(d => d.UsageCount, d => d.UsageCount + 1),
                ct);
        return updated > 0;
    }
}
