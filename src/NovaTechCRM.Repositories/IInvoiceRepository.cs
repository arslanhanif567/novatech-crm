using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invoice?> GetByNumberAsync(string invoiceNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> GetByCustomerAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceSummary>> GetOverdueAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> GetByStatusAsync(InvoiceStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> SearchAsync(string query, CancellationToken ct = default);
    Task<Invoice> CreateAsync(Invoice invoice, CancellationToken ct = default);
    Task<Invoice> UpdateAsync(Invoice invoice, CancellationToken ct = default);
    Task AddPaymentRecordAsync(InvoicePaymentRecord record, CancellationToken ct = default);
    Task<int> GetNextSequenceAsync(int year, CancellationToken ct = default);
}
