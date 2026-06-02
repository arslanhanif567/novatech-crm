using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly NovaTechDbContext _db;

    public NotificationRepository(NovaTechDbContext db) => _db = db;

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlyList<Notification>> GetByCustomerAsync(
        int customerId, int limit = 50, CancellationToken ct = default)
        => await _db.Notifications
            .AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Notification>> GetPendingAsync(CancellationToken ct = default)
        => await _db.Notifications
            .AsNoTracking()
            .Where(n => n.Status == NotificationStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task<Notification> CreateAsync(
        Notification notification, CancellationToken ct = default)
    {
        notification.CreatedAt = DateTime.UtcNow;
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        return notification;
    }

    public async Task<Notification> UpdateAsync(
        Notification notification, CancellationToken ct = default)
    {
        _db.Notifications.Update(notification);
        await _db.SaveChangesAsync(ct);
        return notification;
    }

    public async Task MarkDeliveredAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Notifications
            .Where(n => n.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.Status,      NotificationStatus.Delivered)
                       .SetProperty(n => n.DeliveredAt, DateTime.UtcNow),
                ct);
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default)
    {
        await _db.Notifications
            .Where(n => n.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.Status,       NotificationStatus.Failed)
                       .SetProperty(n => n.FailedAt,     DateTime.UtcNow)
                       .SetProperty(n => n.ErrorMessage, error),
                ct);
    }
}
