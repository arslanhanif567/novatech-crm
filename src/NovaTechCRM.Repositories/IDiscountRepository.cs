using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IDiscountRepository
{
    Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Discount?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Discount>> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Discount>> GetByCustomerTierAsync(
        CustomerTier tier, CancellationToken ct = default);
    Task<Discount> CreateAsync(Discount discount, CancellationToken ct = default);
    Task<Discount> UpdateAsync(Discount discount, CancellationToken ct = default);
    Task<bool> IncrementUsageAsync(Guid discountId, CancellationToken ct = default);
}
