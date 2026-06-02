using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

// Simple in-DB search — we looked at Elasticsearch but the infra team said no (NOVA-45)
// So this does LIKE queries which are slow on large tables. Acceptable for now.
// TODO: at least add full-text indexes (NOVA-56)
public class SearchService
{
    private readonly ICustomerRepository _customerRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ICacheService _cache;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        ICustomerRepository customerRepo,
        IOrderRepository orderRepo,
        IInvoiceRepository invoiceRepo,
        ICacheService cache,
        ILogger<SearchService> logger)
    {
        _customerRepo = customerRepo;
        _orderRepo    = orderRepo;
        _invoiceRepo  = invoiceRepo;
        _cache        = cache;
        _logger       = logger;
    }

    public async Task<GlobalSearchResult> SearchAsync(
        string query, int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new GlobalSearchResult();

        var q          = query.Trim();
        var cacheKey   = $"search_{q.ToLowerInvariant()}_{limit}";
        var cached     = await _cache.GetAsync<GlobalSearchResult>(cacheKey, ct);
        if (cached != null) return cached;

        var result = new GlobalSearchResult { Query = q };

        // run searches in parallel
        var customerTask = _customerRepo.SearchAsync(q, ct);
        var orderTask    = SearchOrdersAsync(q, ct);
        var invoiceTask  = _invoiceRepo.SearchAsync(q, ct);

        await Task.WhenAll(customerTask, orderTask, invoiceTask);

        result.Customers = (await customerTask)
            .Take(limit)
            .Select(c => new SearchResult
            {
                Id       = c.Id.ToString(),
                Type     = "Customer",
                Title    = c.Name,
                Subtitle = c.Email,
                Url      = $"/customers/{c.Id}"
            })
            .ToList();

        result.Orders = (await orderTask)
            .Take(limit)
            .Select(o => new SearchResult
            {
                Id       = o.Id.ToString(),
                Type     = "Order",
                Title    = $"Order {o.Id.ToString()[..8]}",
                Subtitle = $"{o.Status} — {o.TotalAmount:C}",
                Url      = $"/orders/{o.Id}"
            })
            .ToList();

        result.Invoices = (await invoiceTask)
            .Take(limit)
            .Select(i => new SearchResult
            {
                Id       = i.Id.ToString(),
                Type     = "Invoice",
                Title    = i.InvoiceNumber,
                Subtitle = $"{i.Status} — {i.TotalAmount:C}",
                Url      = $"/invoices/{i.Id}"
            })
            .ToList();

        result.TotalCount = result.Customers.Count + result.Orders.Count + result.Invoices.Count;

        // cache for 60s — short TTL since search results stale quickly
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromSeconds(60), ct);

        _logger.LogDebug("Search '{Query}' returned {Count} results", q, result.TotalCount);

        return result;
    }

    private async Task<IEnumerable<Order>> SearchOrdersAsync(string query, CancellationToken ct)
    {
        // orders don't have a search method on repo — filter in memory for now
        // this is bad but nobody has time to add it properly
        if (Guid.TryParse(query, out var guid))
        {
            var order = await _orderRepo.GetByIdAsync(guid, ct);
            return order != null ? new[] { order } : Enumerable.Empty<Order>();
        }

        // can't search by customer ID without joining — return empty
        return Enumerable.Empty<Order>();
    }
}

public class GlobalSearchResult
{
    public string Query { get; set; } = string.Empty;
    public List<SearchResult> Customers { get; set; } = new();
    public List<SearchResult> Orders { get; set; } = new();
    public List<SearchResult> Invoices { get; set; } = new();
    public int TotalCount { get; set; }
}

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
