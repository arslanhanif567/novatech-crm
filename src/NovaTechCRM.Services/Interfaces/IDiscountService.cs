using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface IDiscountService
{
    Task<Discount?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Discount>> GetActiveAsync(CancellationToken ct = default);
    Task<Discount> CreateAsync(Discount discount, CancellationToken ct = default);
    Task<Discount> UpdateAsync(Discount discount, CancellationToken ct = default);
    Task<bool> ValidateAsync(string code, int customerId, decimal orderTotal, CancellationToken ct = default);
    Task<List<DiscountApplication>> ApplyDiscountsAsync(IEnumerable<string> codes, int customerId, decimal orderTotal, CustomerTier customerTier, CancellationToken ct = default);
    Task RecordUsageAsync(Guid discountId, int customerId, Guid orderId, decimal amountDiscounted, CancellationToken ct = default);
}
