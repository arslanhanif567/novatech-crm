using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

// Extracted from CustomerRepository.cs so the interface can be referenced/compiled
// without dragging in the EF implementation. Interface unchanged — compile-fix only.
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(int customerId, CancellationToken ct = default);
    Task<(IReadOnlyList<Customer> Items, int Total)> GetAllAsync(
        int page, int pageSize, string? search = null, CustomerStatus? status = null,
        CustomerTier? tier = null, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> SearchAsync(string query, CancellationToken ct = default);
    Task<Customer> CreateAsync(Customer customer, CancellationToken ct = default);
    Task<Customer> UpdateAsync(Customer customer, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetActiveSinceAsync(DateTime since, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetByTierAsync(CustomerTier tier, CancellationToken ct = default);
    Task BulkUpdateTierAsync(IEnumerable<int> customerIds, CustomerTier tier, CancellationToken ct = default);

    // Legacy — kept for backward compat with old reporting queries
    Task<List<OrderSummary>> GetOrderSummariesAsync(int customerId, CancellationToken ct = default);
}
