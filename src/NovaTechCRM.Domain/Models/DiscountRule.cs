namespace NovaTechCRM.Domain.Models;

/// <summary>
/// Discount category determines priority. Only the single highest-priority
/// matching rule should be applied — rules do NOT stack.
/// Priority: Contract (1) > Promotional (2) > Volume (3) > Default (4)
/// </summary>
public enum DiscountCategory
{
    Contract    = 1,   // Negotiated per-customer contract rate — highest priority
    Promotional = 2,   // Time-limited campaign discount
    Volume      = 3,   // Quantity-based tier discount
    Default     = 4,   // Catch-all discount — lowest priority
}

public class DiscountRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DiscountCategory Category { get; set; }

    /// <summary>
    /// Percentage discount (0–100). E.g. 15 means 15% off.
    /// </summary>
    public decimal DiscountPercent { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
