using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class ShipmentRepository : IShipmentRepository
{
    private readonly NovaTechDbContext _db;

    public ShipmentRepository(NovaTechDbContext db) => _db = db;

    public async Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Shipments
            .Include(s => s.Events.OrderByDescending(e => e.OccurredAt))
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Shipment?> GetByTrackingNumberAsync(
        string trackingNumber, CancellationToken ct = default)
        => await _db.Shipments
            .Include(s => s.Events)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber, ct);

    public async Task<IReadOnlyList<Shipment>> GetByOrderAsync(
        Guid orderId, CancellationToken ct = default)
        => await _db.Shipments
            .Include(s => s.Events)
            .AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Shipment>> GetByCustomerAsync(
        int customerId, CancellationToken ct = default)
        => await _db.Shipments
            .AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Shipment>> GetByStatusAsync(
        ShipmentStatus status, CancellationToken ct = default)
        => await _db.Shipments
            .AsNoTracking()
            .Where(s => s.Status == status)
            .OrderBy(s => s.EstimatedDeliveryAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Shipment>> GetByEstimatedDeliveryRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
        => await _db.Shipments
            .AsNoTracking()
            .Where(s => s.EstimatedDeliveryAt >= from && s.EstimatedDeliveryAt <= to)
            .OrderBy(s => s.EstimatedDeliveryAt)
            .ToListAsync(ct);

    public async Task<Shipment> CreateAsync(Shipment shipment, CancellationToken ct = default)
    {
        shipment.CreatedAt = DateTime.UtcNow;
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task<Shipment> UpdateAsync(Shipment shipment, CancellationToken ct = default)
    {
        shipment.UpdatedAt = DateTime.UtcNow;
        _db.Shipments.Update(shipment);
        await _db.SaveChangesAsync(ct);
        return shipment;
    }

    public async Task AddEventAsync(ShipmentEvent shipmentEvent, CancellationToken ct = default)
    {
        _db.ShipmentEvents.Add(shipmentEvent);
        await _db.SaveChangesAsync(ct);
    }
}
