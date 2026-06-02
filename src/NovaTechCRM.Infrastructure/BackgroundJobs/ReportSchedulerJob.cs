using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Infrastructure.BackgroundJobs;

// Checks for scheduled reports due to run and triggers them.
// Polling every minute is intentionally coarse — scheduled reports are not time-critical.
// NOTE: this does NOT handle cron expression evaluation — IReportService.RunScheduledAsync
// is responsible for computing next run times and filtering what's actually due.
public class ReportSchedulerJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReportSchedulerJob> _logger;
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public ReportSchedulerJob(IServiceProvider services, ILogger<ReportSchedulerJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReportSchedulerJob started");

        // stagger first run by 10s to avoid thundering herd at startup
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
                await reportService.RunScheduledAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReportSchedulerJob failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
