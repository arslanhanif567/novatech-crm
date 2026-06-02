using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditAction action, string entityType, string entityId, string? userId, object? oldValues = null, object? newValues = null, CancellationToken ct = default);
    Task LogAsync(AuditLog entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetEntityHistoryAsync(string entityType, string entityId, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetUserActivityAsync(string userId, DateTime from, DateTime to, CancellationToken ct = default);
    Task FlushBatchAsync(CancellationToken ct = default);
}
