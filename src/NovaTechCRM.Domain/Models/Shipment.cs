namespace NovaTechCRM.Domain.Models;

public enum ShipmentStatus
{
    Pending         = 0,
    LabelCreated    = 1,
    PickedUp        = 2,
    InTransit       = 3,
    OutForDelivery  = 4,
    Delivered       = 5,
    AttemptedDelivery = 6,
    ReturnInitiated = 7,
    Returned        = 8,
    Lost            = 9,
    Cancelled       = 10
}

public enum ShipmentCarrier
{
    FedEx,
    UPS,
    DHL,
    USPS,
    Other
}

public enum ShipmentServiceLevel
{
    Standard,
    Express,
    Overnight,
    SameDay,
    Economy
}

public class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }
    public int CustomerId { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public ShipmentCarrier Carrier { get; set; }
    public ShipmentServiceLevel ServiceLevel { get; set; } = ShipmentServiceLevel.Standard;

    public string? TrackingNumber { get; set; }
    public string? CarrierTrackingUrl { get; set; }
    public string? LabelUrl { get; set; }

    // weight in grams
    public decimal WeightGrams { get; set; }

    // dimensions in cm
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    // addresses stored flat — snapshot at time of shipment
    public string ShipFromName { get; set; } = string.Empty;
    public string ShipFromLine1 { get; set; } = string.Empty;
    public string? ShipFromLine2 { get; set; }
    public string ShipFromCity { get; set; } = string.Empty;
    public string ShipFromState { get; set; } = string.Empty;
    public string ShipFromPostalCode { get; set; } = string.Empty;
    public string ShipFromCountry { get; set; } = string.Empty;

    public string ShipToName { get; set; } = string.Empty;
    public string ShipToLine1 { get; set; } = string.Empty;
    public string? ShipToLine2 { get; set; }
    public string ShipToCity { get; set; } = string.Empty;
    public string ShipToState { get; set; } = string.Empty;
    public string ShipToPostalCode { get; set; } = string.Empty;
    public string ShipToCountry { get; set; } = string.Empty;

    public decimal ShippingCost { get; set; }
    public decimal? InsuredValue { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LabelCreatedAt { get; set; }
    public DateTime? PickedUpAt { get; set; }

    // NOVA-91: EstimatedDeliveryAt stored in local time, not UTC
    // this causes delivery window calculation to shift by UTC offset
    public DateTime? EstimatedDeliveryAt { get; set; }
    public DateTime? ActualDeliveryAt { get; set; }

    public string? SignedBy { get; set; }
    public string? DeliveryNotes { get; set; }

    // tracking events — loaded on demand
    public List<ShipmentEvent> Events { get; set; } = new();

    public List<ShipmentItem> Items { get; set; } = new();

    public bool IsDelivered => Status == ShipmentStatus.Delivered;
    public bool IsLate => EstimatedDeliveryAt.HasValue
                          && !IsDelivered
                          && DateTime.UtcNow > EstimatedDeliveryAt.Value;
}

public class ShipmentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }

    public ShipmentStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;

    // location from carrier feed
    public string? Location { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }

    // NOVA-91: carrier timestamps are local time, we store them as-is
    // should convert to UTC on ingestion but nobody fixed it
    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public string? CarrierEventCode { get; set; }
    public string? RawPayload { get; set; }  // original webhook JSON
}

public class ShipmentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitWeight { get; set; }
}

// returned when carrier API creates the label
public class ShipmentLabelResult
{
    public bool Success { get; set; }
    public string? TrackingNumber { get; set; }
    public string? LabelUrl { get; set; }
    public decimal Cost { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CarrierReferenceId { get; set; }
}

public class ShipmentTrackingResult
{
    public string TrackingNumber { get; set; } = string.Empty;
    public ShipmentStatus CurrentStatus { get; set; }
    public string? CurrentLocation { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public List<ShipmentEvent> Events { get; set; } = new();
    public bool IsDelivered { get; set; }
}
