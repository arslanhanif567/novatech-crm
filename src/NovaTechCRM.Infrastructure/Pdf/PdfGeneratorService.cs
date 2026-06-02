using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services;

namespace NovaTechCRM.Infrastructure.Pdf;

// Generates invoice PDFs using a headless HTML-to-PDF approach.
// We evaluated iTextSharp (licence cost), PdfSharp (no async), and WkHtmlToPdf (native binary pain).
// Settled on calling a self-hosted Gotenberg container over HTTP — stateless, Docker-friendly.
// TODO: add template caching so we don't re-read the Razor template on every call (NOVA-67)
public class GotenbergPdfGeneratorService : IPdfGeneratorService
{
    private readonly HttpClient _http;
    private readonly PdfOptions _opts;
    private readonly IStorageService _storage;
    private readonly ILogger<GotenbergPdfGeneratorService> _logger;

    public GotenbergPdfGeneratorService(
        HttpClient http,
        IOptions<PdfOptions> opts,
        IStorageService storage,
        ILogger<GotenbergPdfGeneratorService> logger)
    {
        _http    = http;
        _opts    = opts.Value;
        _storage = storage;
        _logger  = logger;

        _http.BaseAddress = new Uri(_opts.GotenbergUrl);
        _http.Timeout     = TimeSpan.FromSeconds(30);
    }

    public async Task<string> GenerateInvoicePdfAsync(
        Invoice invoice, CancellationToken ct = default)
    {
        var html = RenderInvoiceHtml(invoice);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(html), "files", "index.html");

        // Gotenberg page margins
        form.Add(new StringContent("0.5in"), "marginTop");
        form.Add(new StringContent("0.5in"), "marginBottom");
        form.Add(new StringContent("0.5in"), "marginLeft");
        form.Add(new StringContent("0.5in"), "marginRight");

        var response = await _http.PostAsync(
            "forms/chromium/convert/html", form, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gotenberg PDF generation failed for invoice {Id}: {Error}",
                invoice.Id, err);
            throw new PdfGenerationException($"PDF generation failed: {err}");
        }

        var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);
        var fileName = $"invoices/{invoice.InvoiceNumber}.pdf";

        var url = await _storage.UploadAsync(fileName, pdfBytes, "application/pdf", ct);

        _logger.LogInformation("Generated PDF for invoice {Number}: {Url}",
            invoice.InvoiceNumber, url);

        return url;
    }

    // Inline HTML template — not ideal but works. Razor templating was considered
    // but adds complexity for what is essentially a static layout.
    private static string RenderInvoiceHtml(Invoice invoice)
    {
        var lineItemRows = string.Join("\n", invoice.LineItems.Select(li => $"""
            <tr>
                <td>{li.Description}</td>
                <td>{li.ProductSku ?? "—"}</td>
                <td style="text-align:right">{li.Quantity}</td>
                <td style="text-align:right">{li.UnitPrice:C}</td>
                <td style="text-align:right">{li.Total:C}</td>
            </tr>
        """));

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8"/>
                <style>
                    body {{ font-family: Arial, sans-serif; font-size: 12px; color: #1a1a1a; margin: 0; }}
                    h1   {{ font-size: 24px; margin-bottom: 4px; }}
                    table {{ width: 100%; border-collapse: collapse; margin-top: 16px; }}
                    th, td {{ padding: 8px 10px; border-bottom: 1px solid #e0e0e0; }}
                    th {{ background: #f5f5f5; text-align: left; font-size: 11px; text-transform: uppercase; }}
                    .totals {{ text-align: right; margin-top: 16px; }}
                    .totals td {{ border: none; padding: 4px 10px; }}
                    .total-row {{ font-weight: bold; font-size: 14px; }}
                </style>
            </head>
            <body>
                <h1>Invoice</h1>
                <p><strong>{invoice.InvoiceNumber}</strong></p>
                <p>Bill To: {invoice.CustomerName} &lt;{invoice.CustomerEmail}&gt;</p>
                <p>Issued: {invoice.IssuedAt:MMMM dd, yyyy} &nbsp;|&nbsp; Due: {invoice.DueAt:MMMM dd, yyyy}</p>

                <table>
                    <thead>
                        <tr>
                            <th>Description</th>
                            <th>SKU</th>
                            <th style="text-align:right">Qty</th>
                            <th style="text-align:right">Unit Price</th>
                            <th style="text-align:right">Total</th>
                        </tr>
                    </thead>
                    <tbody>
                        {lineItemRows}
                    </tbody>
                </table>

                <table class="totals">
                    <tr><td>Subtotal</td><td>{invoice.SubTotal:C}</td></tr>
                    <tr><td>Tax ({invoice.TaxRate:P0})</td><td>{invoice.TaxAmount:C}</td></tr>
                    <tr class="total-row"><td>Total</td><td>{invoice.TotalAmount:C}</td></tr>
                    <tr><td>Amount Paid</td><td>{invoice.AmountPaid:C}</td></tr>
                    <tr><td><strong>Balance Due</strong></td><td><strong>{invoice.AmountDue:C}</strong></td></tr>
                </table>

                <p style="margin-top:32px;font-size:11px;color:#999">
                    Payment terms: {invoice.PaymentTerms} &nbsp;|&nbsp; {invoice.Notes ?? string.Empty}
                </p>
            </body>
            </html>
        """;
    }
}

public class PdfOptions
{
    public string GotenbergUrl { get; set; } = "http://localhost:3000/";
}

public class PdfGenerationException : Exception
{
    public PdfGenerationException(string message) : base(message) { }
}

public interface IStorageService
{
    Task<string> UploadAsync(string path, byte[] data, string contentType, CancellationToken ct = default);
    Task<byte[]> DownloadAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
}
