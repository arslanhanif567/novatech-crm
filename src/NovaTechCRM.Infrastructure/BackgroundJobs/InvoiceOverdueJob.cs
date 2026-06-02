using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Infrastructure.BackgroundJobs;

// Runs daily at midnight UTC to mark issued invoices as overdue and send notifications.
// Simple polling loop — we evaluated Hangfire and Quartz but both felt like overkill
// for two background jobs. Re-evaluate when we have more scheduled work (NOVA-73).
public class InvoiceOverdueJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<InvoiceOverdueJob> _logger;

    public InvoiceOverdueJob(IServiceProvider services, ILogger<InvoiceOverdueJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InvoiceOverdueJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now           = DateTime.UtcNow;
            var nextMidnight  = now.Date.AddDays(1);
            var delay         = nextMidnight - now;

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                using var scope = _services.CreateScope();
                var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
                await invoiceService.ProcessOverdueAsync(stoppingToken);

                _logger.LogInformation("InvoiceOverdueJob completed at {Time}", DateTime.UtcNow);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InvoiceOverdueJob failed");
            }
        }
    }
}
