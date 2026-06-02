using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

[Route("api/shipments")]
[Authorize]
public class ShipmentsController : BaseController
{
    private readonly IShipmentService _shipments;

    public ShipmentsController(IShipmentService shipments) => _shipments = shipments;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var shipment = await _shipments.GetByIdAsync(id, ct);
        if (shipment is null) return NotFound();

        if (!IsAdmin && shipment.CustomerId != CurrentCustomerId)
            return Forbid();

        return Ok(shipment);
    }

    [HttpGet("track/{trackingNumber}")]
    public async Task<IActionResult> Track(string trackingNumber, CancellationToken ct)
    {
        // tracking is public — no auth check needed, tracking numbers aren't secret
        var result = await _shipments.TrackAsync(trackingNumber, ct);
        return Ok(result);
    }

    [HttpGet("customer/{customerId:int}")]
    public async Task<IActionResult> GetByCustomer(int customerId, CancellationToken ct)
    {
        if (!IsAdmin && CurrentCustomerId != customerId)
            return Forbid();

        var shipments = await _shipments.GetByCustomerAsync(customerId, ct);
        return Ok(shipments);
    }

    [HttpGet("order/{orderId:guid}")]
    public async Task<IActionResult> GetByOrder(Guid orderId, CancellationToken ct)
    {
        var shipments = await _shipments.GetByOrderAsync(orderId, ct);
        return Ok(shipments);
    }

    [HttpPost]
    [Authorize(Roles = "admin,fulfillment")]
    public async Task<IActionResult> Create(
        [FromBody] CreateShipmentRequest req, CancellationToken ct)
    {
        var shipment = await _shipments.CreateAsync(
            req.OrderId, req.Carrier, req.ServiceLevel, req.Notes, ct);
        return Created($"/api/shipments/{shipment.Id}", shipment);
    }

    [HttpPost("{id:guid}/label")]
    [Authorize(Roles = "admin,fulfillment")]
    public async Task<IActionResult> GenerateLabel(Guid id, CancellationToken ct)
    {
        var result = await _shipments.GenerateLabelAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Roles = "admin,fulfillment")]
    public async Task<IActionResult> VoidLabel(Guid id, CancellationToken ct)
    {
        await _shipments.VoidLabelAsync(id, ct);
        return Ok(new { message = "Label voided." });
    }

    [HttpGet("pending")]
    [Authorize(Roles = "admin,fulfillment,manager")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var pending = await _shipments.GetPendingFulfillmentAsync(ct);
        return Ok(pending);
    }

    [HttpGet("rates")]
    public async Task<IActionResult> GetRates(
        [FromQuery] string originPostal,
        [FromQuery] string destPostal,
        [FromQuery] string destCountry,
        [FromQuery] decimal weightLbs,
        CancellationToken ct)
    {
        var rates = await _shipments.GetRatesAsync(
            originPostal, destPostal, destCountry, weightLbs, ct);
        return Ok(rates);
    }
}

public record CreateShipmentRequest(
    Guid OrderId,
    string Carrier,
    string ServiceLevel,
    string? Notes
);
