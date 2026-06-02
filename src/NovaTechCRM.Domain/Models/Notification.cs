namespace NovaTechCRM.Domain.Models;

public enum NotificationType
{
    Email,
    Sms,
    PushNotification,
    InApp,
    Webhook
}

public enum NotificationStatus
{
    Queued,
    Sending,
    Sent,
    Delivered,
    Failed,
    Bounced,
    Suppressed  // recipient unsubscribed or marked spam
}

public enum NotificationCategory
{
    Transactional,  // order confirm, invoice, shipping
    Marketing,
    System,         // password reset, 2FA etc
    Alert,          // fraud, low stock
    Digest          // weekly summary
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NotificationType Type { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public NotificationCategory Category { get; set; } = NotificationCategory.Transactional;

    // recipient
    public int? CustomerId { get; set; }
    public string RecipientAddress { get; set; } = string.Empty;  // email or phone
    public string? RecipientName { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }

    // which template was used
    public string? TemplateName { get; set; }
    public Dictionary<string, string> TemplateData { get; set; } = new();

    // references — what triggered this notification
    public Guid? OrderId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid? PaymentId { get; set; }

    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }

    // provider-specific IDs
    public string? ProviderMessageId { get; set; }
    public string? ProviderResponse { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? FailedAt { get; set; }

    public string? FailureReason { get; set; }

    public bool CanRetry => Status == NotificationStatus.Failed
                            && AttemptCount < MaxAttempts;
}

public class NotificationTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public NotificationType Type { get; set; }
    public NotificationCategory Category { get; set; }

    public string SubjectTemplate { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public string? HtmlBodyTemplate { get; set; }

    // Handlebars-style variables — {{customerName}}, {{orderNumber}} etc
    public List<string> RequiredVariables { get; set; } = new();
    public List<string> OptionalVariables { get; set; } = new();

    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    // versioning — we keep old versions for audit
    public int Version { get; set; } = 1;
    public int? PreviousVersionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? LastModifiedByUserId { get; set; }
}

// one per customer per notification type — tracks opt-outs
public class NotificationPreference
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public NotificationType Type { get; set; }
    public NotificationCategory Category { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// stats for the dashboard — aggregated by background job
public class NotificationStats
{
    public DateTime Date { get; set; }
    public NotificationType Type { get; set; }
    public int TotalSent { get; set; }
    public int TotalDelivered { get; set; }
    public int TotalFailed { get; set; }
    public int TotalBounced { get; set; }
    public double DeliveryRate => TotalSent == 0 ? 0 : (double)TotalDelivered / TotalSent;
}
