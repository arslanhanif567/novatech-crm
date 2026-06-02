using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Infrastructure.BackgroundJobs;

// Flushes the in-memory audit batch to the database every 30 seconds.
// AuditService batches writes and flushes at 100 entries; this job is the safety net
// for low-traffic periods where the threshold is never reached.
public class AuditFlushJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AuditFlushJob> _logger;
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public AuditFlushJob(IServiceProvider services, ILogger<AuditFlushJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditFlushJob started — interval {Interval}s",
            _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            try
            {
                using var scope = _services.CreateScope();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
                await audit.FlushBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuditFlushJob encountered an error");
            }
        }
    }
}
