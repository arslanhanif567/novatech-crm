using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

[Route("api/reports")]
[Authorize(Roles = "admin,manager,analyst")]
public class ReportsController : BaseController
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(id, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var reports = await _reports.GetByUserAsync(CurrentUserId, ct);
        return Ok(reports);
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run(
        [FromBody] RunReportRequest req, CancellationToken ct)
    {
        var report = await _reports.RunAsync(
            req.Type, req.Parameters, CurrentUserId,
            req.Format ?? ReportFormat.Json, ct);

        return Ok(report);
    }

    [HttpPost("run-async")]
    public async Task<IActionResult> RunAsync(
        [FromBody] RunReportRequest req, CancellationToken ct)
    {
        // fires report in background — returns report ID immediately
        var report = await _reports.QueueAsync(
            req.Type, req.Parameters, CurrentUserId,
            req.Format ?? ReportFormat.Csv, ct);

        return Accepted($"/api/reports/{report.Id}", new
        {
            reportId = report.Id,
            message  = "Report queued. Poll the report ID for status.",
        });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(id, ct);
        if (report is null) return NotFound();

        if (report.Status != ReportStatus.Completed || string.IsNullOrEmpty(report.OutputUrl))
            return BadRequest(new { error = "Report is not ready for download." });

        return Ok(new { url = report.OutputUrl });
    }

    // ── Schedules ──

    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules(CancellationToken ct)
    {
        var schedules = await _reports.GetSchedulesAsync(ct);
        return Ok(schedules);
    }

    [HttpPost("schedules")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> CreateSchedule(
        [FromBody] CreateScheduleRequest req, CancellationToken ct)
    {
        var schedule = await _reports.CreateScheduleAsync(
            req.Type, req.CronExpression, req.Parameters,
            req.RecipientEmails, CurrentUserId, ct);

        return Created($"/api/reports/schedules/{schedule.Id}", schedule);
    }

    [HttpDelete("schedules/{scheduleId:guid}")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> DeleteSchedule(Guid scheduleId, CancellationToken ct)
    {
        await _reports.DeleteScheduleAsync(scheduleId, ct);
        return NoContent();
    }
}

public record RunReportRequest(
    ReportType Type,
    Dictionary<string, string>? Parameters,
    ReportFormat? Format
);

public record CreateScheduleRequest(
    ReportType Type,
    string CronExpression,
    Dictionary<string, string>? Parameters,
    List<string> RecipientEmails
);
