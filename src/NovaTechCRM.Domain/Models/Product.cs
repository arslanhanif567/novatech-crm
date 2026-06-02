namespace NovaTechCRM.Domain.Models;

public enum ProductStatus
{
    Active,
    Inactive,
    Discontinued,
    ComingSoon,
    OutOfStock
}

public enum ProductType
{
    Physical,
    Digital,
    Service,
    Subscription,
    Bundle
}

// TODO: this whole file needs a refactor pass — ProductCategory was bolted on
// in sprint 19 and the naming is inconsistent with the rest of the domain

public class ProductCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Slug { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // self-referencing nav — not always loaded
    public ProductCategory? ParentCategory { get; set; }
    public List<ProductCategory> SubCategories { get; set; } = new();
    public List<Product> Products { get; set; } = new();
}

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Active;
    public ProductType Type { get; set; } = ProductType.Physical;

    public int? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }

    public decimal BasePrice { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal? CostPrice { get; set; }  // internal — never expose in API

    public string Currency { get; set; } = "USD";

    // physical product dimensions
    public decimal? WeightGrams { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    public string? ImageUrl { get; set; }
    public List<string> AdditionalImageUrls { get; set; } = new();

    // SEO
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? Slug { get; set; }

    // for digital products
    public string? DownloadUrl { get; set; }
    public int? DownloadLimitPerPurchase { get; set; }

    // for subscription products
    public string? BillingInterval { get; set; }  // "monthly", "yearly"
    public int? TrialDays { get; set; }

    public bool IsTaxable { get; set; } = true;
    public string? TaxCode { get; set; }

    public bool TrackInventory { get; set; } = true;
    public bool AllowBackorder { get; set; }

    public List<ProductAttribute> Attributes { get; set; } = new();
    public List<ProductVariant> Variants { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedByUserId { get; set; }

    public decimal EffectivePrice => SalePrice ?? BasePrice;
    public bool IsOnSale => SalePrice.HasValue && SalePrice < BasePrice;

    // Tags stored as comma-separated — I know, I know
    // TODO: proper tagging table (NOVA-44)
    public string? Tags { get; set; }
    public List<string> TagList => Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(t => t.Trim())
                                       .ToList() ?? new();
}

public class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // e.g. Color=Red, Size=XL
    public Dictionary<string, string> OptionValues { get; set; } = new();

    public decimal? PriceOverride { get; set; }
    public decimal? WeightOverride { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public Product? Product { get; set; }
}

public class ProductAttribute
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;  // e.g. "Color"
    public List<string> Values { get; set; } = new();  // e.g. ["Red","Blue","Green"]
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
}
