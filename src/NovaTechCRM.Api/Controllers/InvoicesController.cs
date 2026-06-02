using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

[Route("api/invoices")]
[Authorize]
public class InvoicesController : BaseController
{
    private readonly IInvoiceService _invoices;

    public InvoicesController(IInvoiceService invoices) => _invoices = invoices;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(id, ct);
        if (invoice is null) return NotFound();

        if (!IsAdmin && invoice.CustomerId != CurrentCustomerId)
            return Forbid();

        return Ok(invoice);
    }

    [HttpGet("number/{number}")]
    public async Task<IActionResult> GetByNumber(string number, CancellationToken ct)
    {
        var invoice = await _invoices.GetByNumberAsync(number, ct);
        if (invoice is null) return NotFound();

        if (!IsAdmin && invoice.CustomerId != CurrentCustomerId)
            return Forbid();

        return Ok(invoice);
    }

    [HttpGet("customer/{customerId:int}")]
    public async Task<IActionResult> GetByCustomer(int customerId, CancellationToken ct)
    {
        if (!IsAdmin && CurrentCustomerId != customerId)
            return Forbid();

        var invoices = await _invoices.GetByCustomerAsync(customerId, ct);
        return Ok(invoices);
    }

    [HttpGet("overdue")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> GetOverdue(CancellationToken ct)
    {
        var overdue = await _invoices.GetOverdueAsync(ct);
        return Ok(overdue);
    }

    [HttpPost]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Create(
        [FromBody] CreateInvoiceRequest req, CancellationToken ct)
    {
        var invoice = new Invoice
        {
            CustomerId    = req.CustomerId,
            CustomerName  = req.CustomerName,
            CustomerEmail = req.CustomerEmail,
            Currency      = req.Currency ?? "USD",
            PaymentTerms  = req.PaymentTerms ?? "NET30",
            DueAt         = DateTime.UtcNow.AddDays(req.DueDays ?? 30),
            Notes         = req.Notes,
            LineItems     = req.LineItems.Select(li => new InvoiceLineItem
            {
                Description = li.Description,
                ProductSku  = li.Sku,
                Quantity    = li.Quantity,
                UnitPrice   = li.UnitPrice,
            }).ToList(),
        };

        var created = await _invoices.CreateManualAsync(invoice, ct);
        return Created($"/api/invoices/{created.Id}", created);
    }

    [HttpPost("{id:guid}/issue")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
    {
        var issued = await _invoices.IssueAsync(id, ct);
        return Ok(issued);
    }

    [HttpPost("{id:guid}/send")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct)
    {
        await _invoices.SendAsync(id, ct);
        return Ok(new { message = "Invoice sent." });
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Void(
        Guid id, [FromBody] VoidInvoiceRequest req, CancellationToken ct)
    {
        var voided = await _invoices.VoidAsync(id, req.Reason, ct);
        return Ok(voided);
    }

    /// <summary>
    /// Download invoice as PDF. Generates PDF if not already available.
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(id, ct);
        if (invoice is null) return NotFound();

        // TODO: ownership check missing here — any authenticated user can download
        //       any invoice PDF if they know the GUID. Low-priority since GUIDs are hard to guess
        //       but technically IDOR. See NOVA-83 for the tracked instance in PaymentsController.
        if (!IsAdmin && invoice.CustomerId != CurrentCustomerId)
            return Forbid();

        var url = await _invoices.GeneratePdfAsync(id, ct);
        return Ok(new { url });
    }
}

public record CreateInvoiceRequest(
    int CustomerId,
    string CustomerName,
    string CustomerEmail,
    string? Currency,
    string? PaymentTerms,
    int? DueDays,
    string? Notes,
    List<InvoiceLineItemRequest> LineItems
);

public record InvoiceLineItemRequest(
    string Description,
    string? Sku,
    int Quantity,
    decimal UnitPrice
);

public record VoidInvoiceRequest(string Reason);
