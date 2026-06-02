using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Services;

namespace NovaTechCRM.Api.Controllers;

[Route("api/search")]
[Authorize]
public class SearchController : BaseController
{
    private readonly SearchService _search;

    public SearchController(SearchService search) => _search = search;

    /// <summary>
    /// Global search across customers, orders and invoices.
    /// Min query length: 2 characters. Results capped at 20 per entity type.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { error = "Query must be at least 2 characters." });

        // non-admin users only see their own data — but SearchService currently
        // returns results across all customers. TODO: add customer-scoping (NOVA-57)
        var result = await _search.SearchAsync(q, limit, ct);
        return Ok(result);
    }
}
