using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Infrastructure.BackgroundJobs;

// Releases expired inventory reservations every 5 minutes.
// Reservations expire if the order isn't confirmed within the hold window (default 15 min).
// This is critical to avoid stock appearing unavailable when orders are abandoned.
public class InventoryCleanupJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<InventoryCleanupJob> _logger;
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public InventoryCleanupJob(IServiceProvider services, ILogger<InventoryCleanupJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InventoryCleanupJob started — interval {Interval} min",
            _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            try
            {
                using var scope    = _services.CreateScope();
                var inventoryService = scope.ServiceProvider
                    .GetRequiredService<IInventoryService>();

                var released = await inventoryService.ReleaseExpiredReservationsAsync(stoppingToken);
                if (released > 0)
                    _logger.LogInformation("Released {Count} expired inventory reservations", released);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InventoryCleanupJob failed");
            }
        }
    }
}
