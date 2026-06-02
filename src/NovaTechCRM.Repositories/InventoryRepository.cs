using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly NovaTechDbContext _db;

    public InventoryRepository(NovaTechDbContext db) => _db = db;

    public async Task<Inventory?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Inventory.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Inventory?> GetByProductAsync(
        Guid productId, Guid? variantId, string? warehouseId, CancellationToken ct = default)
    {
        var q = _db.Inventory.AsNoTracking().Where(i => i.ProductId == productId);

        if (variantId.HasValue)
            q = q.Where(i => i.VariantId == variantId.Value);

        if (!string.IsNullOrEmpty(warehouseId))
            q = q.Where(i => i.WarehouseId == warehouseId);

        return await q.FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Inventory>> GetByWarehouseAsync(
        string warehouseId, CancellationToken ct = default)
        => await _db.Inventory
            .AsNoTracking()
            .Where(i => i.WarehouseId == warehouseId)
            .OrderBy(i => i.ProductId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Inventory>> GetLowStockAsync(
        int threshold, CancellationToken ct = default)
        => await _db.Inventory
            .AsNoTracking()
            .Where(i => i.QuantityAvailable <= threshold && !i.IsDiscontinued)
            .OrderBy(i => i.QuantityAvailable)
            .ToListAsync(ct);

    // NOTE: this is a simple EF Core update — no optimistic concurrency here.
    // NOVA-61: the service layer is responsible for the check-reserve logic; we just persist.
    // Proper fix would be a stored procedure with UPDLOCK or row-version concurrency check.
    // TODO: add RowVersion column and handle DbUpdateConcurrencyException (NOVA-61)
    public async Task<Inventory> UpdateAsync(Inventory inventory, CancellationToken ct = default)
    {
        _db.Inventory.Update(inventory);
        await _db.SaveChangesAsync(ct);
        return inventory;
    }

    public async Task<InventoryReservation> CreateReservationAsync(
        InventoryReservation reservation, CancellationToken ct = default)
    {
        _db.InventoryReservations.Add(reservation);
        await _db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<InventoryReservation?> GetReservationAsync(
        Guid reservationId, CancellationToken ct = default)
        => await _db.InventoryReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct);

    public async Task DeleteReservationAsync(Guid reservationId, CancellationToken ct = default)
    {
        await _db.InventoryReservations
            .Where(r => r.Id == reservationId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteExpiredReservationsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _db.InventoryReservations
            .Where(r => r.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
    }
}
