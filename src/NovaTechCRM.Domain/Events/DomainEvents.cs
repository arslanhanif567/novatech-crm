using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Domain.Events;

// Base event — all domain events inherit from this
public abstract class DomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public string EventType => GetType().Name;
}

public class OrderPlacedEvent : DomainEvent
{
    public Order Order { get; }
    public OrderPlacedEvent(Order order) => Order = order;
}

public class OrderFulfilledEvent : DomainEvent
{
    public Order Order { get; }
    public OrderFulfilledEvent(Order order) => Order = order;
}

public class OrderCancelledEvent : DomainEvent
{
    public Order Order { get; }
    public string Reason { get; }
    public OrderCancelledEvent(Order order, string reason)
    {
        Order = order;
        Reason = reason;
    }
}

public class PaymentProcessedEvent : DomainEvent
{
    public Payment Payment { get; }
    public PaymentProcessedEvent(Payment payment) => Payment = payment;
}

public class PaymentFailedEvent : DomainEvent
{
    public Payment Payment { get; }
    public string Reason { get; }
    public PaymentFailedEvent(Payment payment, string reason)
    {
        Payment = payment;
        Reason = reason;
    }
}

public class ShipmentStatusChangedEvent : DomainEvent
{
    public Shipment Shipment { get; }
    public ShipmentStatus OldStatus { get; }
    public ShipmentStatus NewStatus { get; }

    public ShipmentStatusChangedEvent(Shipment shipment, ShipmentStatus old, ShipmentStatus @new)
    {
        Shipment = shipment;
        OldStatus = old;
        NewStatus = @new;
    }
}

// NOVA-61: InventoryReservedEvent is raised BEFORE the DB write commits.
// If two threads raise this event concurrently both see available > 0.
public class InventoryReservedEvent : DomainEvent
{
    public string ProductSku { get; }
    public int QuantityReserved { get; }
    public Guid OrderId { get; }

    public InventoryReservedEvent(string sku, int qty, Guid orderId)
    {
        ProductSku = sku;
        QuantityReserved = qty;
        OrderId = orderId;
    }
}

public class InventoryLowStockEvent : DomainEvent
{
    public string ProductSku { get; }
    public int QuantityAvailable { get; }
    public int ReorderPoint { get; }

    public InventoryLowStockEvent(string sku, int available, int reorderPoint)
    {
        ProductSku = sku;
        QuantityAvailable = available;
        ReorderPoint = reorderPoint;
    }
}

public class CustomerTierUpgradedEvent : DomainEvent
{
    public int CustomerId { get; }
    public CustomerTier OldTier { get; }
    public CustomerTier NewTier { get; }

    public CustomerTierUpgradedEvent(int customerId, CustomerTier oldTier, CustomerTier newTier)
    {
        CustomerId = customerId;
        OldTier = oldTier;
        NewTier = newTier;
    }
}

public class InvoiceOverdueEvent : DomainEvent
{
    public Guid InvoiceId { get; }
    public int CustomerId { get; }
    public decimal AmountDue { get; }
    public int DaysOverdue { get; }

    public InvoiceOverdueEvent(Guid invoiceId, int customerId, decimal amountDue, int daysOverdue)
    {
        InvoiceId = invoiceId;
        CustomerId = customerId;
        AmountDue = amountDue;
        DaysOverdue = daysOverdue;
    }
}
