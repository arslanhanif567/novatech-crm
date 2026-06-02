using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface IReportService
{
    Task<Report> RequestAsync(ReportType type, DateTime periodStart, DateTime periodEnd, string userId, ReportFormat format = ReportFormat.Json, CancellationToken ct = default);
    Task<Report?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Report>> GetByUserAsync(string userId, CancellationToken ct = default);
    Task ProcessPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SalesReportRow>> GetSalesSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<RevenueByProductRow>> GetRevenueByProductAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerRetentionRow>> GetCustomerRetentionAsync(int year, CancellationToken ct = default);
    Task<ReportSchedule> CreateScheduleAsync(ReportSchedule schedule, CancellationToken ct = default);
    Task<IReadOnlyList<ReportSchedule>> GetSchedulesAsync(CancellationToken ct = default);
    Task RunScheduledReportsAsync(CancellationToken ct = default);
}
