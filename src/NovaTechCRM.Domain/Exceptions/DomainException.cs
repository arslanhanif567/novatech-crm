namespace NovaTechCRM.Domain.Exceptions;

public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public DomainException(string code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }
}

public class InsufficientInventoryException : DomainException
{
    public string ProductSku { get; }
    public int Requested { get; }
    public int Available { get; }

    public InsufficientInventoryException(string sku, int requested, int available)
        : base("INSUFFICIENT_INVENTORY",
               $"Cannot reserve {requested} units of '{sku}' — only {available} available.")
    {
        ProductSku = sku;
        Requested = requested;
        Available = available;
    }
}

public class PaymentFailedException : DomainException
{
    public string? ProviderCode { get; }

    public PaymentFailedException(string message, string? providerCode = null)
        : base("PAYMENT_FAILED", message)
    {
        ProviderCode = providerCode;
    }
}

public class ShipmentException : DomainException
{
    public ShipmentException(string message)
        : base("SHIPMENT_ERROR", message) { }
}

public class InvoiceException : DomainException
{
    public InvoiceException(string message)
        : base("INVOICE_ERROR", message) { }
}

public class DiscountException : DomainException
{
    public DiscountException(string message)
        : base("DISCOUNT_ERROR", message) { }
}

public class CustomerNotFoundException : DomainException
{
    public CustomerNotFoundException(int id)
        : base("CUSTOMER_NOT_FOUND", $"Customer {id} not found.") { }
}

public class OrderNotFoundException : DomainException
{
    public OrderNotFoundException(Guid id)
        : base("ORDER_NOT_FOUND", $"Order {id} not found.") { }
}
