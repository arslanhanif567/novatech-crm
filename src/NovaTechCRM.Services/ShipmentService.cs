using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Domain.Exceptions;
using NovaTechCRM.Domain.ValueObjects;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

public class ShipmentService : IShipmentService
{
    private readonly IShipmentRepository _shipmentRepo;
    private readonly IShippingProviderFactory _providerFactory;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly ILogger<ShipmentService> _logger;

    public ShipmentService(
        IShipmentRepository shipmentRepo,
        IShippingProviderFactory providerFactory,
        INotificationService notifications,
        IAuditService audit,
        ILogger<ShipmentService> logger)
    {
        _shipmentRepo   = shipmentRepo;
        _providerFactory = providerFactory;
        _notifications  = notifications;
        _audit          = audit;
        _logger         = logger;
    }

    public async Task<Shipment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _shipmentRepo.GetByIdAsync(id, ct);

    public async Task<Shipment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default) =>
        await _shipmentRepo.GetByOrderIdAsync(orderId, ct);

    public async Task<IReadOnlyList<Shipment>> GetByCustomerAsync(
        int customerId, CancellationToken ct = default) =>
        await _shipmentRepo.GetByCustomerAsync(customerId, ct);

    public async Task<IReadOnlyList<Shipment>> GetLateShipmentsAsync(CancellationToken ct = default)
    {
        var window = DateRange.LastNDaysUtc(30);

        var shipments = await _shipmentRepo.GetByEstimatedDeliveryRangeAsync(
            window.Start, window.End, ct);

        return shipments
            .Where(s => s.IsLate)
            .OrderBy(s => s.EstimatedDeliveryAt)
            .ToList();
    }

    public async Task<Shipment> CreateAsync(Shipment shipment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shipment.ShipToLine1))
            throw new ShipmentException("Delivery address is required.");

        shipment.Status    = ShipmentStatus.Pending;
        shipment.CreatedAt = DateTime.UtcNow;

        var created = await _shipmentRepo.CreateAsync(shipment, ct);

        await _audit.LogAsync(AuditAction.Created, "Shipment", created.Id.ToString(),
            null, newValues: new { created.OrderId, created.Carrier, created.ShipToCity }, ct: ct);

        _logger.LogInformation("Shipment {ShipmentId} created for order {OrderId}",
            created.Id, created.OrderId);

        return created;
    }

    public async Task<ShipmentLabelResult> CreateLabelAsync(
        Guid shipmentId, CancellationToken ct = default)
    {
        var shipment = await _shipmentRepo.GetByIdAsync(shipmentId, ct)
            ?? throw new ShipmentException($"Shipment {shipmentId} not found.");

        if (shipment.Status != ShipmentStatus.Pending)
            throw new ShipmentException($"Cannot create label for shipment in status {shipment.Status}.");

        var provider = _providerFactory.GetProvider(shipment.Carrier);
        var result   = await provider.CreateLabelAsync(shipment, ct);

        if (!result.Success)
        {
            _logger.LogError("Label creation failed for shipment {ShipmentId}: {Error}",
                shipmentId, result.ErrorMessage);
            return result;
        }

        shipment.TrackingNumber    = result.TrackingNumber;
        shipment.CarrierTrackingUrl = result.CarrierReferenceId; // TODO: build proper URL per carrier
        shipment.LabelUrl          = result.LabelUrl;
        shipment.ShippingCost      = result.Cost;
        shipment.Status            = ShipmentStatus.LabelCreated;
        shipment.LabelCreatedAt    = DateTime.UtcNow;

        await _shipmentRepo.UpdateAsync(shipment, ct);

        _logger.LogInformation("Label created for shipment {ShipmentId}: {TrackingNumber}",
            shipmentId, result.TrackingNumber);

        return result;
    }

    public async Task<ShipmentTrackingResult> RefreshTrackingAsync(
        Guid shipmentId, CancellationToken ct = default)
    {
        var shipment = await _shipmentRepo.GetByIdAsync(shipmentId, ct)
            ?? throw new ShipmentException($"Shipment {shipmentId} not found.");

        if (string.IsNullOrWhiteSpace(shipment.TrackingNumber))
            throw new ShipmentException("Shipment has no tracking number yet.");

        var provider = _providerFactory.GetProvider(shipment.Carrier);
        var result   = await provider.TrackAsync(shipment.TrackingNumber, ct);

        if (result.IsDelivered && shipment.Status != ShipmentStatus.Delivered)
        {
            await UpdateStatusAsync(shipmentId, ShipmentStatus.Delivered,
                result.CurrentLocation, ct);
        }
        else if (result.CurrentStatus != shipment.Status)
        {
            await UpdateStatusAsync(shipmentId, result.CurrentStatus,
                result.CurrentLocation, ct);
        }

        // store new events
        foreach (var evt in result.Events)
        {
            evt.ShipmentId = shipmentId;
            await _shipmentRepo.AddEventAsync(evt, ct);
        }

        return result;
    }

    public async Task UpdateStatusAsync(
        Guid shipmentId, ShipmentStatus status, string? location,
        CancellationToken ct = default)
    {
        var shipment = await _shipmentRepo.GetByIdAsync(shipmentId, ct)
            ?? throw new ShipmentException($"Shipment {shipmentId} not found.");

        var oldStatus = shipment.Status;
        shipment.Status = status;

        if (status == ShipmentStatus.Delivered)
        {
            // NOVA-91: ActualDeliveryAt uses DateTime.UtcNow (correct)
            // but EstimatedDeliveryAt was stored in local time on creation
            // so SLA comparisons are off
            shipment.ActualDeliveryAt = DateTime.UtcNow;
        }

        await _shipmentRepo.UpdateAsync(shipment, ct);

        await _shipmentRepo.AddEventAsync(new ShipmentEvent
        {
            ShipmentId   = shipmentId,
            Status       = status,
            Description  = $"Status changed to {status}",
            Location     = location,
            OccurredAt   = DateTime.UtcNow, // correct — but EstimatedDelivery still broken
            RecordedAt   = DateTime.UtcNow
        }, ct);

        if (status == ShipmentStatus.Delivered || status == ShipmentStatus.OutForDelivery)
        {
            await _notifications.SendShipmentUpdateAsync(shipment, ct);
        }

        _logger.LogInformation(
            "Shipment {ShipmentId} status: {Old} -> {New}",
            shipmentId, oldStatus, status);
    }

    public async Task HandleCarrierWebhookAsync(
        ShipmentCarrier carrier, string payload, CancellationToken ct = default)
    {
        _logger.LogDebug("Received {Carrier} webhook: {Payload}", carrier, payload);

        // TODO: implement per-carrier webhook parsing (NOVA-53)
        // For now we just log it and do nothing
        // FedEx is the only one we actually use in prod — UPS and DHL are placeholders
        await Task.CompletedTask;
    }
}

// marker interface — implementations live in Infrastructure layer
public interface IShippingProviderFactory
{
    IShippingProvider GetProvider(ShipmentCarrier carrier);
}

public interface IShippingProvider
{
    Task<ShipmentLabelResult> CreateLabelAsync(Shipment shipment, CancellationToken ct);
    Task<ShipmentTrackingResult> TrackAsync(string trackingNumber, CancellationToken ct);
}
