using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IShipmentRepository
{
    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken ct = default);
    Task<IReadOnlyList<Shipment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Shipment>> GetByCustomerAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<Shipment>> GetByStatusAsync(ShipmentStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<Shipment>> GetByEstimatedDeliveryRangeAsync(
        DateTime from, DateTime to, CancellationToken ct = default);
    Task<Shipment> CreateAsync(Shipment shipment, CancellationToken ct = default);
    Task<Shipment> UpdateAsync(Shipment shipment, CancellationToken ct = default);
    Task AddEventAsync(ShipmentEvent shipmentEvent, CancellationToken ct = default);
}
