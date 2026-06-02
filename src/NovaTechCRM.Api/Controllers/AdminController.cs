using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

[Route("api/admin")]
[Authorize(Roles = "admin")]
public class AdminController : BaseController
{
    private readonly ICustomerService _customers;
    private readonly IAuditService _audit;
    private readonly ICacheService _cache;
    private readonly IInventoryService _inventory;

    public AdminController(
        ICustomerService customers,
        IAuditService audit,
        ICacheService cache,
        IInventoryService inventory)
    {
        _customers = customers;
        _audit     = audit;
        _cache     = cache;
        _inventory = inventory;
    }

    [HttpPost("recalculate-all-tiers")]
    public async Task<IActionResult> RecalculateAllTiers(CancellationToken ct)
    {
        var updated = await _customers.RecalculateAllTiersAsync(ct);
        return Ok(new { message = $"Tier recalculation complete.", customersUpdated = updated });
    }

    [HttpPost("cache/flush")]
    public async Task<IActionResult> FlushCache(
        [FromQuery] string? prefix = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(prefix))
            await _cache.RemoveByPrefixAsync(prefix, ct);
        else
            await _cache.RemoveByPrefixAsync("", ct); // flush all

        return Ok(new { message = "Cache flushed.", prefix = prefix ?? "(all)" });
    }

    [HttpPost("audit/flush")]
    public async Task<IActionResult> FlushAudit(CancellationToken ct)
    {
        await _audit.FlushBatchAsync(ct);
        return Ok(new { message = "Audit batch flushed." });
    }

    [HttpGet("audit/{entityType}/{entityId}")]
    public async Task<IActionResult> GetEntityHistory(
        string entityType, string entityId,
        [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var history = await _audit.GetEntityHistoryAsync(entityType, entityId, limit, ct);
        return Ok(history);
    }

    [HttpGet("audit/user/{userId}")]
    public async Task<IActionResult> GetUserActivity(
        string userId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to   = null,
        CancellationToken ct       = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end   = to   ?? DateTime.UtcNow;

        var activity = await _audit.GetUserActivityAsync(userId, start, end, ct);
        return Ok(activity);
    }

    [HttpGet("inventory/low-stock")]
    public async Task<IActionResult> GetLowStock(
        [FromQuery] int threshold = 10, CancellationToken ct = default)
    {
        var items = await _inventory.GetLowStockAsync(threshold, ct);
        return Ok(items);
    }

    [HttpPost("inventory/cleanup-reservations")]
    public async Task<IActionResult> CleanupReservations(CancellationToken ct)
    {
        var released = await _inventory.ReleaseExpiredReservationsAsync(ct);
        return Ok(new { released });
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health() => Ok(new
    {
        status    = "healthy",
        timestamp = DateTime.UtcNow,
        version   = typeof(AdminController).Assembly
                        .GetName().Version?.ToString() ?? "unknown"
    });
}
