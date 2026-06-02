using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services.Interfaces;

public interface IShipmentService
{
    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Shipment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Shipment>> GetByCustomerAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<Shipment>> GetLateShipmentsAsync(CancellationToken ct = default);
    Task<Shipment> CreateAsync(Shipment shipment, CancellationToken ct = default);
    Task<ShipmentLabelResult> CreateLabelAsync(Guid shipmentId, CancellationToken ct = default);
    Task<ShipmentTrackingResult> RefreshTrackingAsync(Guid shipmentId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid shipmentId, ShipmentStatus status, string? location, CancellationToken ct = default);
    Task HandleCarrierWebhookAsync(ShipmentCarrier carrier, string payload, CancellationToken ct = default);
}
