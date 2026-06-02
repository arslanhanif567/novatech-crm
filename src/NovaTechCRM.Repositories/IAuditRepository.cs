using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IAuditRepository
{
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType, string entityId, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetByUserAsync(
        string userId, DateTime from, DateTime to, CancellationToken ct = default);
    Task BulkInsertAsync(IEnumerable<AuditLog> entries, CancellationToken ct = default);
}
