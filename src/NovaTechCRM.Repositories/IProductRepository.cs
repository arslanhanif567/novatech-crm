using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);
    Task<(IReadOnlyList<Product> Items, int Total)> GetAllAsync(
        int page, int pageSize, ProductCategory? category = null,
        bool? activeOnly = true, string? search = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchAsync(string query, CancellationToken ct = default);
    Task<Product> CreateAsync(Product product, CancellationToken ct = default);
    Task<Product> UpdateAsync(Product product, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ProductVariant?> GetVariantAsync(Guid variantId, CancellationToken ct = default);
}
