using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly NovaTechDbContext _db;
    private readonly string _connectionString;

    public AuditRepository(NovaTechDbContext db, string connectionString)
    {
        _db               = db;
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(
        string entityType, string entityId, int limit = 50, CancellationToken ct = default)
        => await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AuditLog>> GetByUserAsync(
        string userId, DateTime from, DateTime to, CancellationToken ct = default)
        => await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.OccurredAt >= from && a.OccurredAt <= to)
            .OrderByDescending(a => a.OccurredAt)
            .ToListAsync(ct);

    // Uses SqlBulkCopy for performance — EF Core AddRange is too slow for high-volume audit writes.
    // AuditService batches 100 entries before calling this, so we keep DB round trips low.
    public async Task BulkInsertAsync(IEnumerable<AuditLog> entries, CancellationToken ct = default)
    {
        var list = entries.ToList();
        if (!list.Any()) return;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var bulkCopy = new SqlBulkCopy(conn)
        {
            DestinationTableName = "AuditLogs",
            BatchSize            = 500,
            BulkCopyTimeout      = 30
        };

        // column mappings — must match table schema exactly
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.Action),       "Action");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.EntityType),    "EntityType");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.EntityId),      "EntityId");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.UserId),        "UserId");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.OldValuesJson), "OldValuesJson");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.NewValuesJson), "NewValuesJson");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.OccurredAt),    "OccurredAt");
        bulkCopy.ColumnMappings.Add(nameof(AuditLog.PartitionMonth),"PartitionMonth");

        var table = BuildDataTable(list);
        await bulkCopy.WriteToServerAsync(table, ct);
    }

    private static System.Data.DataTable BuildDataTable(IEnumerable<AuditLog> entries)
    {
        var dt = new System.Data.DataTable();
        dt.Columns.Add("Action",        typeof(int));
        dt.Columns.Add("EntityType",    typeof(string));
        dt.Columns.Add("EntityId",      typeof(string));
        dt.Columns.Add("UserId",        typeof(string));
        dt.Columns.Add("OldValuesJson", typeof(string));
        dt.Columns.Add("NewValuesJson", typeof(string));
        dt.Columns.Add("OccurredAt",    typeof(DateTime));
        dt.Columns.Add("PartitionMonth",typeof(int));

        foreach (var e in entries)
        {
            dt.Rows.Add(
                (int)e.Action,
                e.EntityType,
                e.EntityId,
                (object?)e.UserId        ?? DBNull.Value,
                (object?)e.OldValuesJson ?? DBNull.Value,
                (object?)e.NewValuesJson ?? DBNull.Value,
                e.OccurredAt,
                e.PartitionMonth);
        }

        return dt;
    }
}
