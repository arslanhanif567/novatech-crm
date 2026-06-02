namespace NovaTechCRM.Domain.Models;

public enum AddressType
{
    Billing,
    Shipping,
    Both
}

// Standalone address record — customers can have multiple saved addresses.
// NOTE: Order and Invoice snapshot address fields inline (denormalized).
// This table is only for the customer's address book.
public class Address
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int CustomerId { get; set; }
    public AddressType Type { get; set; } = AddressType.Shipping;

    public string? Alias { get; set; }  // e.g. "Home", "Office"

    public string RecipientName { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Phone { get; set; }

    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }

    // coords — populated by geocoding service (when it works)
    // TODO: geocoding job is flaky, only ~60% of addresses have coords (NOVA-49)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public string ToSingleLine() =>
        string.Join(", ", new[] { Line1, Line2, City, State, PostalCode, Country }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
}
