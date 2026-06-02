using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

// Batches audit writes to reduce DB pressure.
// Flush happens every 30s via background job OR when batch reaches 100 entries.
// WARNING: entries in-memory batch are lost if process crashes — acceptable trade-off per ops team.
public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepo;
    private readonly ILogger<AuditService> _logger;

    private readonly List<AuditLog> _batch = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int FlushThreshold = 100;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented     = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditService(IAuditRepository auditRepo, ILogger<AuditService> logger)
    {
        _auditRepo = auditRepo;
        _logger    = logger;
    }

    public async Task LogAsync(
        AuditAction action,
        string entityType,
        string entityId,
        string? userId,
        object? oldValues  = null,
        object? newValues  = null,
        CancellationToken ct = default)
    {
        var entry = new AuditLog
        {
            Action        = action,
            EntityType    = entityType,
            EntityId      = entityId,
            UserId        = userId,
            OldValuesJson = oldValues != null ? JsonSerializer.Serialize(oldValues, _jsonOpts) : null,
            NewValuesJson = newValues != null ? JsonSerializer.Serialize(newValues, _jsonOpts) : null,
            OccurredAt    = DateTime.UtcNow,
            PartitionMonth = DateTime.UtcNow.Month
        };

        await LogAsync(entry, ct);
    }

    public async Task LogAsync(AuditLog entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _batch.Add(entry);

            if (_batch.Count >= FlushThreshold)
                await FlushInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AuditLog>> GetEntityHistoryAsync(
        string entityType, string entityId, int limit = 50, CancellationToken ct = default)
    {
        return await _auditRepo.GetByEntityAsync(entityType, entityId, limit, ct);
    }

    public async Task<IReadOnlyList<AuditLog>> GetUserActivityAsync(
        string userId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _auditRepo.GetByUserAsync(userId, from, to, ct);
    }

    public async Task FlushBatchAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await FlushInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task FlushInternalAsync(CancellationToken ct)
    {
        if (!_batch.Any()) return;

        var toFlush = _batch.ToList();
        _batch.Clear();

        try
        {
            await _auditRepo.BulkInsertAsync(toFlush, ct);
            _logger.LogDebug("Flushed {Count} audit entries", toFlush.Count);
        }
        catch (Exception ex)
        {
            // put them back — risk of duplication on retry but better than losing data
            _batch.InsertRange(0, toFlush);
            _logger.LogError(ex, "Failed to flush {Count} audit entries", toFlush.Count);
            throw;
        }
    }
}
