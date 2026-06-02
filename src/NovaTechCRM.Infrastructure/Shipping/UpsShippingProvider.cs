using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Shipping;

// UPS Developer API (2023 OAuth version) — secondary carrier, used when FedEx rates
// are higher by > 15% or for specific east-coast next-day routes where UPS is faster.
// Rate comparison logic is in ShipmentService, not here.
public class UpsShippingProvider : IShippingProvider
{
    private readonly HttpClient _http;
    private readonly UpsOptions _opts;
    private readonly ILogger<UpsShippingProvider> _logger;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UpsShippingProvider(
        HttpClient http,
        IOptions<UpsOptions> opts,
        ILogger<UpsShippingProvider> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(opts.Value.IsSandbox
            ? "https://wwwcie.ups.com/api/"
            : "https://onlinetools.ups.com/api/");
    }

    public async Task<ShippingRateResult> GetRatesAsync(
        ShippingRateRequest req, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);

        var payload = new
        {
            RateRequest = new
            {
                Request            = new { SubVersion = "2205" },
                Shipment           = new
                {
                    Shipper          = MapAddress(req.From, _opts.AccountNumber),
                    ShipTo           = MapAddress(req.To),
                    ShipFrom         = MapAddress(req.From),
                    Service          = new { Code = MapServiceCode(req.ServiceLevel) },
                    Package          = new
                    {
                        PackagingType = new { Code = "02" },
                        PackageWeight = new { UnitOfMeasurement = new { Code = "LBS" }, Weight = req.WeightLbs.ToString("F1") },
                        Dimensions    = new
                        {
                            UnitOfMeasurement = new { Code = "IN" },
                            Length = ((int)req.LengthIn).ToString(),
                            Width  = ((int)req.WidthIn).ToString(),
                            Height = ((int)req.HeightIn).ToString(),
                        }
                    }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "rating/v2205/Rate")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("UPS rate request failed: {Status} {Body}",
                response.StatusCode, body);
            return new ShippingRateResult(false, 0, 0, 0, req.ServiceLevel, 0, body);
        }

        using var doc  = JsonDocument.Parse(body);
        var ratedShipment = doc.RootElement
            .GetProperty("RateResponse")
            .GetProperty("RatedShipment");

        var total = decimal.Parse(
            ratedShipment.GetProperty("TotalCharges").GetProperty("MonetaryValue").GetString()!);

        return new ShippingRateResult(
            Success:       true,
            BaseRate:      total * 0.87m,
            FuelSurcharge: total * 0.13m,
            TotalRate:     total,
            ServiceLevel:  req.ServiceLevel,
            EstimatedDays: EstimateDays(req.ServiceLevel));
    }

    public async Task<ShipmentLabelResult> CreateLabelAsync(
        ShipmentLabelRequest req, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);

        // UPS Shipping API (previously Ship API)
        var payload = new
        {
            ShipmentRequest = new
            {
                Request          = new { SubVersion = "2205" },
                Shipment         = new
                {
                    Shipper      = MapAddress(req.From, _opts.AccountNumber),
                    ShipTo       = MapAddress(req.To),
                    ShipFrom     = MapAddress(req.From),
                    Service      = new { Code = MapServiceCode(req.ServiceLevel) },
                    PaymentInformation = new
                    {
                        ShipmentCharge = new
                        {
                            Type       = "01",
                            BillShipper = new { AccountNumber = _opts.AccountNumber }
                        }
                    },
                    Package      = new
                    {
                        PackagingType        = new { Code = "02" },
                        PackageWeight        = new { UnitOfMeasurement = new { Code = "LBS" }, Weight = req.WeightLbs.ToString("F1") },
                        ReferenceNumber      = new { Code = "PM", Value = req.ReferenceNumber },
                    }
                },
                LabelSpecification = new
                {
                    LabelImageFormat = new { Code = "PDF" },
                    LabelStockSize   = new { Height = "6", Width = "4" },
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "shipments/v2205/ship")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("UPS label creation failed: {Body}", body);
            return new ShipmentLabelResult(false, Error: body);
        }

        using var doc       = JsonDocument.Parse(body);
        var shipResult      = doc.RootElement
                                 .GetProperty("ShipmentResponse")
                                 .GetProperty("ShipmentResults");

        var trackingNumber  = shipResult
            .GetProperty("PackageResults")
            .GetProperty("TrackingNumber").GetString();

        var labelBase64     = shipResult
            .GetProperty("PackageResults")
            .GetProperty("ShippingLabel")
            .GetProperty("GraphicImage").GetString();

        var labelPdf        = labelBase64 != null
            ? Convert.FromBase64String(labelBase64)
            : null;

        return new ShipmentLabelResult(
            Success:        true,
            TrackingNumber: trackingNumber,
            LabelPdf:       labelPdf);
    }

    public async Task<TrackingResult> TrackAsync(
        string trackingNumber, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"track/v1/details/{trackingNumber}?locale=en_US&returnSignature=false");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new TrackingResult(false, "Unknown", Error: body);

        using var doc = JsonDocument.Parse(body);
        var shipment  = doc.RootElement
            .GetProperty("trackResponse")
            .GetProperty("shipment")[0];

        var status = shipment
            .GetProperty("package")[0]
            .GetProperty("currentStatus")
            .GetProperty("description").GetString() ?? "Unknown";

        return new TrackingResult(Success: true, Status: status);
    }

    public async Task<bool> VoidLabelAsync(
        string trackingNumber, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);
        var request = new HttpRequestMessage(
            HttpMethod.Delete, $"shipments/v2205/void/cancel/{trackingNumber}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            return _accessToken;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "security/v1/oauth/token")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            })
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        using var doc  = JsonDocument.Parse(body);
        _accessToken   = doc.RootElement.GetProperty("access_token").GetString()!;
        _tokenExpiry   = DateTime.UtcNow.AddSeconds(
            doc.RootElement.GetProperty("expires_in").GetInt32() - 120);

        return _accessToken;
    }

    private static object MapAddress(ShippingAddress addr, string? accountNumber = null)
    {
        var obj = new Dictionary<string, object>
        {
            ["Name"]    = addr.Name,
            ["Address"] = new
            {
                AddressLine       = new[] { addr.Line1 },
                City              = addr.City,
                StateProvinceCode = addr.State,
                PostalCode        = addr.PostalCode,
                CountryCode       = addr.Country,
            }
        };
        if (accountNumber != null)
            obj["ShipperNumber"] = accountNumber;
        return obj;
    }

    private static string MapServiceCode(string level) => level.ToUpperInvariant() switch
    {
        "OVERNIGHT"   => "01",
        "2DAY"        => "02",
        "GROUND"      => "03",
        "3DAY"        => "12",
        _ => "03"
    };

    private static int EstimateDays(string level) => level.ToUpperInvariant() switch
    {
        "OVERNIGHT" => 1,
        "2DAY"      => 2,
        "3DAY"      => 3,
        _ => 5
    };
}

public class UpsOptions
{
    public string ClientId     { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccountNumber{ get; set; } = string.Empty;
    public bool   IsSandbox    { get; set; } = true;
}
