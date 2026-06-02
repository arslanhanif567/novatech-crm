using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

[Route("api/customers")]
[Authorize]
public class CustomersController : BaseController
{
    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers) => _customers = customers;

    [HttpGet]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] CustomerStatus? status = null,
        [FromQuery] CustomerTier? tier = null,
        CancellationToken ct = default)
    {
        var (items, total) = await _customers.GetAllAsync(page, pageSize, search, status, tier, ct);
        Response.Headers.Append("X-Total-Count", total.ToString());
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        // non-admin users can only fetch themselves
        if (!IsAdmin && CurrentCustomerId != id)
            return Forbid();

        var customer = await _customers.GetByIdAsync(id, ct);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerRequest req, CancellationToken ct)
    {
        var customer = new Customer
        {
            Name   = req.Name,
            Email  = req.Email,
            Phone  = req.Phone,
            Tier   = CustomerTier.Standard,
            Status = CustomerStatus.Active,
        };

        var created = await _customers.CreateAsync(customer, ct);
        return Created($"/api/customers/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] UpdateCustomerRequest req, CancellationToken ct)
    {
        if (!IsAdmin && CurrentCustomerId != id)
            return Forbid();

        var existing = await _customers.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.Name  = req.Name ?? existing.Name;
        existing.Phone = req.Phone ?? existing.Phone;

        // only admins can change billing address
        if (IsAdmin && req.BillingAddress != null)
        {
            existing.BillingAddressLine1 = req.BillingAddress.Line1;
            existing.BillingCity         = req.BillingAddress.City;
            existing.BillingState        = req.BillingAddress.State;
            existing.BillingPostalCode   = req.BillingAddress.PostalCode;
            existing.BillingCountry      = req.BillingAddress.Country;
        }

        var updated = await _customers.UpdateAsync(existing, ct);
        return Ok(updated);
    }

    [HttpGet("{id:int}/stats")]
    public async Task<IActionResult> GetStats(int id, CancellationToken ct)
    {
        if (!IsAdmin && CurrentCustomerId != id)
            return Forbid();

        var stats = await _customers.GetStatsAsync(id, ct);
        return stats is null ? NotFound() : Ok(stats);
    }

    [HttpPost("{id:int}/recalculate-tier")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RecalculateTier(int id, CancellationToken ct)
    {
        var newTier = await _customers.RecalculateTierAsync(id, ct);
        return Ok(new { customerId = id, tier = newTier.ToString() });
    }
}

public record CreateCustomerRequest(
    string Name,
    string Email,
    string? Phone
);

public record UpdateCustomerRequest(
    string? Name,
    string? Phone,
    AddressRequest? BillingAddress
);

public record AddressRequest(
    string Line1,
    string City,
    string State,
    string PostalCode,
    string Country
);
