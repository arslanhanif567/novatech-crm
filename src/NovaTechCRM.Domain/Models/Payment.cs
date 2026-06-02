namespace NovaTechCRM.Domain.Models;

public enum PaymentStatus
{
    Pending     = 0,
    Processing  = 1,
    Succeeded   = 2,
    Failed      = 3,
    Cancelled   = 4,
    Refunded    = 5,
    PartiallyRefunded = 6,
    Disputed    = 7,
    Chargeback  = 8
}

public enum PaymentProvider
{
    Stripe,
    PayPal,
    Braintree,
    BankTransfer,
    Check,
    Cash,
    Credit  // internal credit balance
}

public enum PaymentType
{
    Charge,
    Refund,
    Payout,
    Adjustment
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int CustomerId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? OrderId { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentProvider Provider { get; set; }
    public PaymentType Type { get; set; } = PaymentType.Charge;

    public decimal Amount { get; set; }
    public decimal? RefundedAmount { get; set; }
    public string Currency { get; set; } = "USD";

    // provider-specific IDs
    public string? ProviderPaymentId { get; set; }
    public string? ProviderChargeId { get; set; }
    public string? ProviderRefundId { get; set; }
    public string? ProviderCustomerId { get; set; }

    // card info — last 4 only, never full number
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
    public string? CardExpiryMonth { get; set; }
    public string? CardExpiryYear { get; set; }

    public Guid? PaymentMethodId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? RefundedAt { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public string? Description { get; set; }
    public string? StatementDescriptor { get; set; }

    // raw provider webhook payload for debugging
    // TODO: move this to a separate table, too big for inline storage
    public string? RawProviderPayload { get; set; }

    public string? ProcessedByUserId { get; set; }
    public string? IpAddress { get; set; }

    // nested refunds
    public List<PaymentRefund> Refunds { get; set; } = new();

    public decimal NetAmount => Amount - (RefundedAmount ?? 0);
    public bool IsSettled => Status == PaymentStatus.Succeeded;
    public bool IsFullyRefunded => RefundedAmount.HasValue && RefundedAmount >= Amount;
}

public class PaymentRefund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ProviderRefundId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Processing;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string InitiatedByUserId { get; set; } = string.Empty;
}

public class PaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CustomerId { get; set; }

    public PaymentProvider Provider { get; set; }

    // card details
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }  // visa, mastercard, amex etc
    public int? CardExpiryMonth { get; set; }
    public int? CardExpiryYear { get; set; }
    public string? CardHolderName { get; set; }

    // provider token — never log or expose
    public string? ProviderToken { get; set; }
    public string? ProviderCustomerId { get; set; }

    public bool IsDefault { get; set; }
    public bool IsExpired => CardExpiryYear.HasValue && CardExpiryMonth.HasValue
        && new DateTime(CardExpiryYear.Value, CardExpiryMonth.Value, 1) < DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    // billing address for this card
    public string? BillingLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }
}

// used for the payments list endpoint
public class PaymentSummary
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? InvoiceNumber { get; set; }
}
