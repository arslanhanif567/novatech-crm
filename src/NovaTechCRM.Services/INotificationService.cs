using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services;

public interface INotificationService
{
    Task SendOrderConfirmationAsync(Order order, CancellationToken ct = default);
    Task SendFraudAlertAsync(Order order, FraudCheckResult result, CancellationToken ct = default);
    // Declared on the interface so callers (e.g. InvoiceService) can use them; the
    // concrete NotificationService already implements both. Compile-fix only — no
    // behaviour change.
    Task SendInvoiceAsync(Invoice invoice, CancellationToken ct = default);
    Task SendInvoiceOverdueAsync(Invoice invoice, CancellationToken ct = default);
}
