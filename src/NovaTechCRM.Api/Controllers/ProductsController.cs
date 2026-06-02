using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

[Route("api/products")]
public class ProductsController : BaseController
{
    private readonly IProductService _products;

    public ProductsController(IProductService products) => _products = products;

    // Public — product catalog is not behind auth
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] ProductCategory? category = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var (items, total) = await _products.GetAllAsync(page, pageSize, category, search, ct);
        Response.Headers.Append("X-Total-Count", total.ToString());
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("sku/{sku}")]
    public async Task<IActionResult> GetBySku(string sku, CancellationToken ct)
    {
        var product = await _products.GetBySkuAsync(sku, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Roles = "admin,catalog_manager")]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest req, CancellationToken ct)
    {
        var product = new Product
        {
            Name        = req.Name,
            Sku         = req.Sku,
            Description = req.Description,
            Category    = req.Category,
            BasePrice   = req.BasePrice,
            CostPrice   = req.CostPrice,
            IsActive    = true,
        };

        var created = await _products.CreateAsync(product, ct);
        return Created($"/api/products/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin,catalog_manager")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateProductRequest req, CancellationToken ct)
    {
        var updated = await _products.UpdateAsync(id, req.Name, req.Description,
            req.BasePrice, req.IsActive, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _products.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{id:guid}/inventory")]
    [Authorize]
    public async Task<IActionResult> GetInventory(Guid id, CancellationToken ct)
    {
        var inventory = await _products.GetInventoryAsync(id, ct);
        return Ok(inventory);
    }
}

public record CreateProductRequest(
    string Name,
    string Sku,
    string? Description,
    ProductCategory Category,
    decimal BasePrice,
    decimal? CostPrice
);

public record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? BasePrice,
    bool? IsActive
);
