using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IReportRepository
{
    Task<Report?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Report>> GetByUserAsync(
        string userId, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<Report>> GetPendingAsync(CancellationToken ct = default);
    Task<Report> CreateAsync(Report report, CancellationToken ct = default);
    Task<Report> UpdateAsync(Report report, CancellationToken ct = default);
    Task<IReadOnlyList<ReportSchedule>> GetActiveSchedulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReportSchedule>> GetDueSchedulesAsync(CancellationToken ct = default);
    Task<ReportSchedule> CreateScheduleAsync(ReportSchedule schedule, CancellationToken ct = default);
    Task<ReportSchedule> UpdateScheduleAsync(ReportSchedule schedule, CancellationToken ct = default);
    Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default);
}
