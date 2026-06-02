using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Infrastructure.Email;
using NovaTechCRM.Infrastructure.Sms;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Infrastructure.Notifications;

// Orchestrates email + SMS delivery and persists notification records.
// Template rendering is intentionally kept simple (string interpolation) — a proper
// template engine was planned but never prioritised. See NOVA-69 for the Fluid/Scriban spike.
public class NotificationService : INotificationService
{
    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly INotificationRepository _notificationRepo;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IEmailSender email,
        ISmsSender sms,
        INotificationRepository notificationRepo,
        ILogger<NotificationService> logger)
    {
        _email           = email;
        _sms             = sms;
        _notificationRepo = notificationRepo;
        _logger          = logger;
    }

    public async Task SendPaymentConfirmationAsync(
        Payment payment, CancellationToken ct = default)
    {
        // we need customer email — caller is responsible for ensuring the payment has it
        // or we fall back to a no-op with a warning
        if (string.IsNullOrEmpty(payment.CustomerEmail))
        {
            _logger.LogWarning(
                "Payment {PaymentId} has no customer email — skipping confirmation",
                payment.Id);
            return;
        }

        var notification = await _notificationRepo.CreateAsync(new Notification
        {
            CustomerId = payment.CustomerId,
            Type       = NotificationType.Email,
            Category   = NotificationCategory.Payment,
            Subject    = $"Payment Confirmed — {payment.Amount:C}",
            Body       = BuildPaymentConfirmationBody(payment),
            Status     = NotificationStatus.Pending,
        }, ct);

        try
        {
            await _email.SendAsync(new EmailMessage
            {
                To       = payment.CustomerEmail,
                Subject  = notification.Subject!,
                HtmlBody = notification.Body,
            }, ct);

            await _notificationRepo.MarkDeliveredAsync(notification.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send payment confirmation for {PaymentId}", payment.Id);
            await _notificationRepo.MarkFailedAsync(notification.Id, ex.Message, ct);
        }
    }

    public async Task SendInvoiceAsync(Invoice invoice, CancellationToken ct = default)
    {
        var notification = await _notificationRepo.CreateAsync(new Notification
        {
            CustomerId = invoice.CustomerId,
            Type       = NotificationType.Email,
            Category   = NotificationCategory.Invoice,
            Subject    = $"Invoice {invoice.InvoiceNumber} — {invoice.TotalAmount:C} due {invoice.DueAt:MMM dd}",
            Body       = BuildInvoiceEmailBody(invoice),
            Status     = NotificationStatus.Pending,
        }, ct);

        try
        {
            var attachments = new List<EmailAttachment>();

            // attach PDF if generated
            if (!string.IsNullOrEmpty(invoice.PdfUrl))
            {
                // TODO: download PDF from storage and attach — currently just links in body (NOVA-70)
            }

            await _email.SendAsync(new EmailMessage
            {
                To          = invoice.CustomerEmail,
                ToName      = invoice.CustomerName,
                Subject     = notification.Subject!,
                HtmlBody    = notification.Body,
                Attachments = attachments,
            }, ct);

            await _notificationRepo.MarkDeliveredAsync(notification.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invoice {Number}", invoice.InvoiceNumber);
            await _notificationRepo.MarkFailedAsync(notification.Id, ex.Message, ct);
        }
    }

    public async Task SendInvoiceOverdueAsync(Invoice invoice, CancellationToken ct = default)
    {
        var daysLate = (int)(DateTime.UtcNow - invoice.DueAt).TotalDays;

        var notification = await _notificationRepo.CreateAsync(new Notification
        {
            CustomerId = invoice.CustomerId,
            Type       = NotificationType.Email,
            Category   = NotificationCategory.Invoice,
            Subject    = $"OVERDUE: Invoice {invoice.InvoiceNumber} — {invoice.AmountDue:C} ({daysLate} days late)",
            Body       = BuildOverdueEmailBody(invoice, daysLate),
            Status     = NotificationStatus.Pending,
        }, ct);

        try
        {
            await _email.SendAsync(new EmailMessage
            {
                To       = invoice.CustomerEmail,
                ToName   = invoice.CustomerName,
                Subject  = notification.Subject!,
                HtmlBody = notification.Body,
            }, ct);

            await _notificationRepo.MarkDeliveredAsync(notification.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send overdue notice for {Number}", invoice.InvoiceNumber);
            await _notificationRepo.MarkFailedAsync(notification.Id, ex.Message, ct);
        }
    }

    public async Task SendShipmentUpdateAsync(
        Shipment shipment, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(shipment.CustomerPhone))
        {
            _logger.LogDebug("No phone for shipment {Id} — skipping SMS update", shipment.Id);
            return;
        }

        var smsBody = $"NovaTech: Your order {shipment.OrderId.ToString()[..8]}... — {message}. " +
                      $"Track: {shipment.TrackingNumber}";

        try
        {
            await _sms.SendAsync(shipment.CustomerPhone, smsBody, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMS update failed for shipment {Id}", shipment.Id);
        }
    }

    // --- Template helpers (inline HTML — TODO: move to Razor/Fluid templates, NOVA-69) ---

    private static string BuildPaymentConfirmationBody(Payment payment) => $"""
        <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;">
            <h2 style="color:#1a1a1a">Payment Confirmed</h2>
            <p>Your payment of <strong>{payment.Amount:C}</strong> has been processed successfully.</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
                <tr><td style="padding:8px;color:#666">Amount</td><td style="padding:8px;font-weight:bold">{payment.Amount:C} {payment.Currency}</td></tr>
                <tr><td style="padding:8px;color:#666">Card</td><td style="padding:8px">{payment.CardBrand} ending {payment.CardLast4}</td></tr>
                <tr><td style="padding:8px;color:#666">Date</td><td style="padding:8px">{payment.ProcessedAt:MMMM dd, yyyy}</td></tr>
                <tr><td style="padding:8px;color:#666">Reference</td><td style="padding:8px;font-family:monospace">{payment.Id.ToString()[..8].ToUpperInvariant()}</td></tr>
            </table>
            <p style="color:#999;font-size:12px">NovaTech CRM · support@novatech.io</p>
        </div>
        """;

    private static string BuildInvoiceEmailBody(Invoice invoice) => $"""
        <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;">
            <h2 style="color:#1a1a1a">Invoice {invoice.InvoiceNumber}</h2>
            <p>Please find your invoice attached. Payment is due by <strong>{invoice.DueAt:MMMM dd, yyyy}</strong>.</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
                <tr><td style="padding:8px;color:#666">Invoice #</td><td style="padding:8px;font-weight:bold">{invoice.InvoiceNumber}</td></tr>
                <tr><td style="padding:8px;color:#666">Amount Due</td><td style="padding:8px;font-weight:bold;color:#c0392b">{invoice.AmountDue:C}</td></tr>
                <tr><td style="padding:8px;color:#666">Due Date</td><td style="padding:8px">{invoice.DueAt:MMMM dd, yyyy}</td></tr>
                <tr><td style="padding:8px;color:#666">Terms</td><td style="padding:8px">{invoice.PaymentTerms}</td></tr>
            </table>
            {(invoice.PdfUrl != null ? $"<p><a href=\"{invoice.PdfUrl}\">Download PDF</a></p>" : "")}
            <p style="color:#999;font-size:12px">NovaTech CRM · billing@novatech.io</p>
        </div>
        """;

    private static string BuildOverdueEmailBody(Invoice invoice, int daysLate) => $"""
        <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;">
            <div style="background:#fee;border:1px solid #fcc;padding:12px;border-radius:4px;margin-bottom:16px">
                <strong style="color:#c0392b">⚠ Payment {daysLate} days overdue</strong>
            </div>
            <h2 style="color:#1a1a1a">Invoice {invoice.InvoiceNumber} is Overdue</h2>
            <p>Your payment of <strong>{invoice.AmountDue:C}</strong> was due on {invoice.DueAt:MMMM dd, yyyy}
               and is now {daysLate} days past due.</p>
            <p>Please settle this balance immediately to avoid service interruption.</p>
            <p style="color:#999;font-size:12px">NovaTech CRM · billing@novatech.io</p>
        </div>
        """;
}
