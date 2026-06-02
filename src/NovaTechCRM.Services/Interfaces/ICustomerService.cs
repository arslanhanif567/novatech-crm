using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface ICustomerService
{
    Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerDashboard>> GetAllDashboardsAsync(CancellationToken ct = default);
    Task<CustomerDashboard> GetDashboardAsync(int customerId, CancellationToken ct = default);
    Task<Customer> CreateAsync(Customer customer, CancellationToken ct = default);
    Task<Customer> UpdateAsync(Customer customer, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<bool> EvaluateTierAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> SearchAsync(string query, CancellationToken ct = default);
}
