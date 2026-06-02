namespace NovaTechCRM.Domain.Models;

public enum DiscountType
{
    PercentageOff,
    FixedAmountOff,
    FreeShipping,
    BuyXGetY,
    TieredPercentage  // different % based on order value
}

public enum DiscountScope
{
    OrderTotal,
    SpecificProducts,
    SpecificCategories,
    SpecificCustomers,
    CustomerTier
}

public enum DiscountStatus
{
    Draft,
    Active,
    Paused,
    Expired,
    Depleted  // usage limit reached
}

// NOVA-58: DiscountConflictResolution only applies when a SINGLE stackable discount
// is involved. When multiple are applied, the resolution strategy is ignored and
// discounts are applied additively in insertion order — can cause discounts to
// stack beyond the intended cap. See DiscountService.ApplyDiscountsAsync.
public enum DiscountConflictResolution
{
    UseHighest,
    UseLowest,
    UseFirst,
    UseLast,
    Additive  // stack all of them
}

public class Discount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DiscountType Type { get; set; }
    public DiscountScope Scope { get; set; } = DiscountScope.OrderTotal;
    public DiscountStatus Status { get; set; } = DiscountStatus.Draft;

    // the actual discount value
    public decimal Value { get; set; }  // percent (0-100) or fixed amount
    public decimal? MaxDiscountAmount { get; set; }  // cap for percentage discounts

    public decimal? MinimumOrderAmount { get; set; }
    public decimal? MinimumQuantity { get; set; }

    // validity window
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // usage limits
    public int? UsageLimitTotal { get; set; }
    public int? UsageLimitPerCustomer { get; set; }
    public int UsageCount { get; set; }

    public bool IsStackable { get; set; }
    public DiscountConflictResolution ConflictResolution { get; set; } = DiscountConflictResolution.UseHighest;

    // tier-based discount rules — for DiscountType.TieredPercentage
    public List<TieredDiscountRule> TieredRules { get; set; } = new();

    // scope filters
    public List<string> ApplicableProductSkus { get; set; } = new();
    public List<int> ApplicableCategoryIds { get; set; } = new();
    public List<int> ApplicableCustomerIds { get; set; } = new();
    public List<CustomerTier> ApplicableCustomerTiers { get; set; } = new();

    // BuyXGetY config
    public int? BuyQuantity { get; set; }
    public int? GetQuantity { get; set; }
    public string? GetProductSku { get; set; }

    public bool IsFirstOrderOnly { get; set; }
    public bool IsSingleUse { get; set; }  // one-time code, not reusable

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedByUserId { get; set; }

    public bool IsActive => Status == DiscountStatus.Active
                            && (StartsAt == null || StartsAt <= DateTime.UtcNow)
                            && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow)
                            && (UsageLimitTotal == null || UsageCount < UsageLimitTotal);
}

public class TieredDiscountRule
{
    public int Id { get; set; }
    public Guid DiscountId { get; set; }
    public decimal MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public decimal DiscountPercent { get; set; }
}

public class DiscountApplication
{
    public Guid DiscountId { get; set; }
    public string DiscountCode { get; set; } = string.Empty;
    public string DiscountName { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public DiscountType Type { get; set; }
    public bool WasApplied { get; set; }
    public string? RejectionReason { get; set; }
}

public class DiscountUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DiscountId { get; set; }
    public int CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public decimal AmountDiscounted { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
}
