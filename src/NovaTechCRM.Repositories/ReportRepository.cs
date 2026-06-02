using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class ReportRepository : IReportRepository
{
    private readonly NovaTechDbContext _db;

    public ReportRepository(NovaTechDbContext db) => _db = db;

    public async Task<Report?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Report>> GetByUserAsync(
        string userId, int limit = 20, CancellationToken ct = default)
        => await _db.Reports
            .AsNoTracking()
            .Where(r => r.RequestedByUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Report>> GetPendingAsync(CancellationToken ct = default)
        => await _db.Reports
            .AsNoTracking()
            .Where(r => r.Status == ReportStatus.Pending || r.Status == ReportStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<Report> CreateAsync(Report report, CancellationToken ct = default)
    {
        report.CreatedAt = DateTime.UtcNow;
        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        return report;
    }

    public async Task<Report> UpdateAsync(Report report, CancellationToken ct = default)
    {
        _db.Reports.Update(report);
        await _db.SaveChangesAsync(ct);
        return report;
    }

    public async Task<IReadOnlyList<ReportSchedule>> GetActiveSchedulesAsync(
        CancellationToken ct = default)
        => await _db.ReportSchedules
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReportSchedule>> GetDueSchedulesAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.ReportSchedules
            .AsNoTracking()
            .Where(s => s.IsActive && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .ToListAsync(ct);
    }

    public async Task<ReportSchedule> CreateScheduleAsync(
        ReportSchedule schedule, CancellationToken ct = default)
    {
        schedule.CreatedAt = DateTime.UtcNow;
        _db.ReportSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);
        return schedule;
    }

    public async Task<ReportSchedule> UpdateScheduleAsync(
        ReportSchedule schedule, CancellationToken ct = default)
    {
        _db.ReportSchedules.Update(schedule);
        await _db.SaveChangesAsync(ct);
        return schedule;
    }

    public async Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default)
    {
        await _db.ReportSchedules
            .Where(s => s.Id == scheduleId)
            .ExecuteDeleteAsync(ct);
    }
}
