using System.Collections.Concurrent;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;

namespace NovaTechCRM.Services;

public interface IReportingService
{
    Task<CustomerDashboard?> GetCustomerDashboardAsync(int customerId, CancellationToken ct);
}

public class ReportingService : IReportingService
{
    private readonly ICustomerRepository _customerRepo;

    private static readonly ConcurrentDictionary<int, CustomerDashboard> _reportingCache = new();

    public ReportingService(ICustomerRepository customerRepo)
    {
        _customerRepo = customerRepo;
    }

    public async Task<CustomerDashboard?> GetCustomerDashboardAsync(int customerId, CancellationToken ct)
    {
        if (_reportingCache.TryGetValue(customerId, out var cached))
            return cached;

        var customer = await _customerRepo.GetByIdAsync(customerId, ct);
        if (customer is null) return null;

        var orders = await _customerRepo.GetOrderSummariesAsync(customerId, ct);

        var dashboard = new CustomerDashboard
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            TotalOrders = orders.Count,
            TotalRevenue = orders.Sum(o => o.Total),
            AverageOrderValue = orders.Count > 0 ? orders.Average(o => o.Total) : 0,
            RecentOrders = orders.Take(10).ToList(),
        };

        _reportingCache[customerId] = dashboard;

        return dashboard;
    }
}
