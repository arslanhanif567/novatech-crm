using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetByCustomerAsync(
        int customerId, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetPendingAsync(CancellationToken ct = default);
    Task<Notification> CreateAsync(Notification notification, CancellationToken ct = default);
    Task<Notification> UpdateAsync(Notification notification, CancellationToken ct = default);
    Task MarkDeliveredAsync(Guid id, CancellationToken ct = default);
    Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default);
}
