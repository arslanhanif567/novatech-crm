using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly NovaTechDbContext _db;

    public ProductRepository(NovaTechDbContext db) => _db = db;

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Products
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default)
        => await _db.Products
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Sku == sku, ct);

    public async Task<(IReadOnlyList<Product> Items, int Total)> GetAllAsync(
        int page, int pageSize, ProductCategory? category = null,
        bool? activeOnly = true, string? search = null, CancellationToken ct = default)
    {
        var q = _db.Products.AsNoTracking();

        if (category.HasValue)
            q = q.Where(p => p.Category == category.Value);

        if (activeOnly == true)
            q = q.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<Product>> SearchAsync(
        string query, CancellationToken ct = default)
        => await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive &&
                        (p.Name.Contains(query) || p.Sku.Contains(query) ||
                         p.Description != null && p.Description.Contains(query)))
            .Take(50)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<Product> CreateAsync(Product product, CancellationToken ct = default)
    {
        product.CreatedAt = DateTime.UtcNow;
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return product;
    }

    public async Task<Product> UpdateAsync(Product product, CancellationToken ct = default)
    {
        product.UpdatedAt = DateTime.UtcNow;
        _db.Products.Update(product);
        await _db.SaveChangesAsync(ct);
        return product;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // hard delete only if no orders reference this product
        // caller is responsible for checking before calling
        var deleted = await _db.Products
            .Where(p => p.Id == id)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<ProductVariant?> GetVariantAsync(
        Guid variantId, CancellationToken ct = default)
        => await _db.ProductVariants
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == variantId, ct);
}
