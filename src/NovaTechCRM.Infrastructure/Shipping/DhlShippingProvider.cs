using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Shipping;

// DHL Express API — used exclusively for international shipments.
// Domestic shipments always go to FedEx or UPS.
// NOTE: DHL requires a separate account contract for each country of origin.
// Currently only US-origin is configured. See NOVA-82 for adding EU origin.
public class DhlShippingProvider : IShippingProvider
{
    private readonly HttpClient _http;
    private readonly DhlOptions _opts;
    private readonly ILogger<DhlShippingProvider> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DhlShippingProvider(
        HttpClient http,
        IOptions<DhlOptions> opts,
        ILogger<DhlShippingProvider> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(opts.Value.IsSandbox
            ? "https://express.api.dhl.com/mydhlapi/test/"
            : "https://express.api.dhl.com/mydhlapi/");

        var creds = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{opts.Value.Username}:{opts.Value.Password}"));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", creds);
    }

    public async Task<ShippingRateResult> GetRatesAsync(
        ShippingRateRequest req, CancellationToken ct = default)
    {
        var qs = $"accountNumber={_opts.AccountNumber}" +
                 $"&originCountryCode={req.From.Country}" +
                 $"&originPostalCode={req.From.PostalCode}" +
                 $"&destinationCountryCode={req.To.Country}" +
                 $"&destinationPostalCode={req.To.PostalCode}" +
                 $"&weight={req.WeightLbs:F1}" +
                 $"&length={(int)req.LengthIn}" +
                 $"&width={(int)req.WidthIn}" +
                 $"&height={(int)req.HeightIn}" +
                 $"&plannedShippingDateAndTime={DateTime.UtcNow.AddDays(1):yyyy-MM-ddTHH:mm:ss} GMT+00:00" +
                 $"&isCustomsDeclarable=false&unitOfMeasurement=imperial";

        var response = await _http.GetAsync($"rates?{qs}", ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("DHL rate request failed: {Status}", response.StatusCode);
            return new ShippingRateResult(false, 0, 0, 0, req.ServiceLevel, 0, body);
        }

        using var doc = JsonDocument.Parse(body);
        var products  = doc.RootElement.GetProperty("products");

        if (products.GetArrayLength() == 0)
            return new ShippingRateResult(false, 0, 0, 0, req.ServiceLevel, 0, "No rates available.");

        var first     = products[0];
        var totalCharge = first.GetProperty("totalPrice")[0];
        var total     = totalCharge.GetProperty("price").GetDecimal();

        return new ShippingRateResult(
            Success:       true,
            BaseRate:      total * 0.80m,
            FuelSurcharge: total * 0.20m,
            TotalRate:     total,
            ServiceLevel:  req.ServiceLevel,
            EstimatedDays: 5);
    }

    public async Task<ShipmentLabelResult> CreateLabelAsync(
        ShipmentLabelRequest req, CancellationToken ct = default)
    {
        var payload = new
        {
            plannedShippingDateAndTime = $"{DateTime.UtcNow.AddDays(1):yyyy-MM-ddTHH:mm:ss} GMT+00:00",
            pickup                     = new { isRequested = false },
            productCode                = "P",
            accounts                   = new[] { new { typeCode = "shipper", number = _opts.AccountNumber } },
            customerDetails            = new
            {
                shipperDetails   = MapAddress(req.From),
                receiverDetails  = MapAddress(req.To),
            },
            content                    = new
            {
                packages = new[]
                {
                    new
                    {
                        weight        = req.WeightLbs,
                        dimensions    = new { length = (int)10, width = (int)8, height = (int)4 },
                        customerReferences = new[] { new { value = req.ReferenceNumber } }
                    }
                },
                isCustomsDeclarable = false,
                description         = "Merchandise",
                incoterm            = "DAP",
                unitOfMeasurement   = "imperial",
            },
            outputImageProperties = new
            {
                printerDPI         = 300,
                encodingFormat     = "pdf",
                imageOptions       = new[] { new { typeCode = "label", templateName = "ECOM26_84CI001" } }
            }
        };

        var content  = new StringContent(
            JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("shipments", content, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DHL label creation failed: {Body}", body);
            return new ShipmentLabelResult(false, Error: body);
        }

        using var doc       = JsonDocument.Parse(body);
        var trackingNumber  = doc.RootElement
            .GetProperty("packages")[0]
            .GetProperty("trackingNumber").GetString();

        var labelBase64     = doc.RootElement
            .GetProperty("documents")[0]
            .GetProperty("content").GetString();

        return new ShipmentLabelResult(
            Success:        true,
            TrackingNumber: trackingNumber,
            LabelPdf:       labelBase64 != null ? Convert.FromBase64String(labelBase64) : null);
    }

    public async Task<TrackingResult> TrackAsync(
        string trackingNumber, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"shipments/{trackingNumber}/tracking", ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new TrackingResult(false, "Unknown", Error: body);

        using var doc = JsonDocument.Parse(body);
        var shipments = doc.RootElement.GetProperty("shipments");

        if (shipments.GetArrayLength() == 0)
            return new TrackingResult(false, "Not found");

        var status = shipments[0]
            .GetProperty("status")
            .GetProperty("description").GetString() ?? "Unknown";

        return new TrackingResult(Success: true, Status: status);
    }

    public Task<bool> VoidLabelAsync(string trackingNumber, CancellationToken ct = default)
    {
        // DHL Express does not support voiding labels via API — must call customer service.
        _logger.LogWarning("DHL label void requested for {Tracking} but is not supported via API",
            trackingNumber);
        return Task.FromResult(false);
    }

    private static object MapAddress(ShippingAddress addr) => new
    {
        postalAddress = new
        {
            postalCode   = addr.PostalCode,
            cityName     = addr.City,
            countryCode  = addr.Country,
            addressLine1 = addr.Line1,
        },
        contactInformation = new
        {
            fullName = addr.Name,
        }
    };
}

public class DhlOptions
{
    public string Username     { get; set; } = string.Empty;
    public string Password     { get; set; } = string.Empty;
    public string AccountNumber{ get; set; } = string.Empty;
    public bool   IsSandbox    { get; set; } = true;
}
