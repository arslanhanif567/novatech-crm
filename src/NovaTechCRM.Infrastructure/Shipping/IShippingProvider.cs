namespace NovaTechCRM.Infrastructure.Shipping;

public interface IShippingProvider
{
    Task<ShippingRateResult> GetRatesAsync(ShippingRateRequest request, CancellationToken ct = default);
    Task<ShipmentLabelResult> CreateLabelAsync(ShipmentLabelRequest request, CancellationToken ct = default);
    Task<TrackingResult> TrackAsync(string trackingNumber, CancellationToken ct = default);
    Task<bool> VoidLabelAsync(string trackingNumber, CancellationToken ct = default);
}

public record ShippingRateRequest(
    ShippingAddress From,
    ShippingAddress To,
    decimal WeightLbs,
    decimal LengthIn,
    decimal WidthIn,
    decimal HeightIn,
    string ServiceLevel = "GROUND"
);

public record ShipmentLabelRequest(
    ShippingAddress From,
    ShippingAddress To,
    decimal WeightLbs,
    string ServiceLevel,
    string ReferenceNumber,
    bool   SignatureRequired = false
);

public record ShippingAddress(
    string Name,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country
);

public record ShippingRateResult(
    bool    Success,
    decimal BaseRate,
    decimal FuelSurcharge,
    decimal TotalRate,
    string  ServiceLevel,
    int     EstimatedDays,
    string? Error = null
);

public record ShipmentLabelResult(
    bool    Success,
    string? TrackingNumber = null,
    byte[]? LabelPdf       = null,
    decimal? Cost          = null,
    string? Error          = null
);

public record TrackingResult(
    bool   Success,
    string Status,
    string? Location        = null,
    DateTime? EstimatedDelivery = null,
    IReadOnlyList<TrackingEvent>? Events = null,
    string? Error = null
);

public record TrackingEvent(
    DateTime OccurredAt,
    string   Description,
    string?  Location
);
