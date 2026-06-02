using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface IInvoiceService
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invoice?> GetByNumberAsync(string invoiceNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> GetByCustomerAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceSummary>> GetOverdueAsync(CancellationToken ct = default);
    Task<Invoice> CreateFromOrderAsync(Order order, CancellationToken ct = default);
    Task<Invoice> CreateManualAsync(Invoice invoice, CancellationToken ct = default);
    Task<Invoice> IssueAsync(Guid invoiceId, CancellationToken ct = default);
    Task<Invoice> RecordPaymentAsync(Guid invoiceId, decimal amount, Guid paymentId, CancellationToken ct = default);
    Task<Invoice> VoidAsync(Guid invoiceId, string reason, CancellationToken ct = default);
    Task<string> GeneratePdfAsync(Guid invoiceId, CancellationToken ct = default);
    Task SendAsync(Guid invoiceId, CancellationToken ct = default);
    Task ProcessOverdueAsync(CancellationToken ct = default);
}
