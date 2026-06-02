namespace NovaTechCRM.Domain.Models;

public enum ReportType
{
    SalesSummary,
    RevenueByProduct,
    RevenueByCustomer,
    OrderFulfillmentRate,
    InventoryStatus,
    CustomerAcquisition,
    CustomerRetention,
    PaymentReconciliation,
    ShippingPerformance,
    FraudAnalysis,
    TaxSummary,
    AuditTrail,
    Custom
}

public enum ReportStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Expired
}

public enum ReportFormat
{
    Json,
    Csv,
    Excel,
    Pdf
}

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public ReportType Type { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Queued;
    public ReportFormat Format { get; set; } = ReportFormat.Json;

    // date range for the report data
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    // filters — stored as JSON
    public string? FiltersJson { get; set; }

    // result
    public string? ResultUrl { get; set; }  // S3/blob URL for large results
    public string? ResultJson { get; set; } // inline for small reports
    public long? FileSizeBytes { get; set; }
    public int? RowCount { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? ExecutionTime => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt - StartedAt
        : null;

    public DateTime? ExpiresAt { get; set; }

    public string RequestedByUserId { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }

    // for scheduled reports
    public Guid? ScheduleId { get; set; }
}

public class ReportSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public ReportType ReportType { get; set; }
    public ReportFormat Format { get; set; } = ReportFormat.Pdf;

    // cron expression — e.g. "0 8 * * 1" = every Monday at 8am
    public string CronExpression { get; set; } = string.Empty;
    public string Timezone { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;

    // who gets the report emailed
    public List<string> RecipientEmails { get; set; } = new();

    public string? FiltersJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public int RunCount { get; set; }
    public int FailureCount { get; set; }
}

// individual metric rows in a sales report
public class SalesReportRow
{
    public DateTime Date { get; set; }
    public int OrderCount { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal Discounts { get; set; }
    public decimal Returns { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class RevenueByProductRow
{
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal MarginPercent { get; set; }
}

public class CustomerRetentionRow
{
    public string Cohort { get; set; } = string.Empty;  // e.g. "2024-Q1"
    public int NewCustomers { get; set; }
    public int ActiveMonth1 { get; set; }
    public int ActiveMonth2 { get; set; }
    public int ActiveMonth3 { get; set; }
    public int ActiveMonth6 { get; set; }
    public int ActiveMonth12 { get; set; }

    // percentages
    public double RetentionMonth1 => NewCustomers == 0 ? 0 : (double)ActiveMonth1 / NewCustomers;
    public double RetentionMonth3 => NewCustomers == 0 ? 0 : (double)ActiveMonth3 / NewCustomers;
    public double RetentionMonth12 => NewCustomers == 0 ? 0 : (double)ActiveMonth12 / NewCustomers;
}
