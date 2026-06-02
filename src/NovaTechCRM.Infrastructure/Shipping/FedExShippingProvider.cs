using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Shipping;

// FedEx REST API (v1) — primary shipping provider.
// Auth uses OAuth2 client_credentials, token valid for 3600s.
// FedEx changed their API from SOAP to REST in 2022; we migrated in Q3 2023.
// Old SOAP wrappers deleted in commit a3f91bc if you need them for reference.
public class FedExShippingProvider : IShippingProvider
{
    private readonly HttpClient _http;
    private readonly FedExOptions _opts;
    private readonly ILogger<FedExShippingProvider> _logger;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FedExShippingProvider(
        HttpClient http,
        IOptions<FedExOptions> opts,
        ILogger<FedExShippingProvider> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(opts.Value.IsSandbox
            ? "https://apis-sandbox.fedex.com/"
            : "https://apis.fedex.com/");
    }

    public async Task<ShippingRateResult> GetRatesAsync(
        ShippingRateRequest req, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);
        var payload = new
        {
            accountNumber      = new { value = _opts.AccountNumber },
            requestedShipment  = new
            {
                shipper           = MapAddress(req.From),
                recipient         = MapAddress(req.To),
                pickupType        = "DROPOFF_AT_FEDEX_LOCATION",
                serviceType       = MapServiceLevel(req.ServiceLevel),
                packagingType     = "YOUR_PACKAGING",
                requestedPackageLineItems = new[]
                {
                    new
                    {
                        weight       = new { units = "LB", value = req.WeightLbs },
                        dimensions   = new
                        {
                            length = (int)req.LengthIn,
                            width  = (int)req.WidthIn,
                            height = (int)req.HeightIn,
                            units  = "IN",
                        }
                    }
                }
            }
        };

        var response = await PostAsync("rate/v1/rates/quotes", payload, token, ct);

        if (response.TryGetProperty("errors", out var errors))
        {
            var msg = errors[0].GetProperty("message").GetString();
            return new ShippingRateResult(false, 0, 0, 0, req.ServiceLevel, 0, msg);
        }

        var rateDetail = response
            .GetProperty("output")
            .GetProperty("rateReplyDetails")[0]
            .GetProperty("ratedShipmentDetails")[0]
            .GetProperty("totalNetCharge");

        var total = rateDetail.GetDecimal();

        return new ShippingRateResult(
            Success:       true,
            BaseRate:      total * 0.85m,
            FuelSurcharge: total * 0.15m,
            TotalRate:     total,
            ServiceLevel:  req.ServiceLevel,
            EstimatedDays: EstimateDays(req.ServiceLevel));
    }

    public async Task<ShipmentLabelResult> CreateLabelAsync(
        ShipmentLabelRequest req, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);
        var payload = new
        {
            labelResponseOptions  = "URL_ONLY",
            requestedShipment     = new
            {
                shipper           = MapAddress(req.From),
                recipients        = new[] { MapAddress(req.To) },
                serviceType       = MapServiceLevel(req.ServiceLevel),
                packagingType     = "YOUR_PACKAGING",
                pickupType        = "USE_SCHEDULED_PICKUP",
                shippingChargesPayment = new
                {
                    paymentType = "SENDER",
                    payor       = new { responsibleParty = new { accountNumber = new { value = _opts.AccountNumber } } }
                },
                requestedPackageLineItems = new[]
                {
                    new
                    {
                        weight      = new { units = "LB", value = (double)req.WeightLbs },
                        customerReferences = new[]
                        {
                            new { customerReferenceType = "CUSTOMER_REFERENCE", value = req.ReferenceNumber }
                        }
                    }
                },
                labelSpecification = new { labelFormatType = "COMMON2D", imageType = "PDF" },
            },
            accountNumber = new { value = _opts.AccountNumber }
        };

        var response = await PostAsync("ship/v1/shipments", payload, token, ct);

        if (response.TryGetProperty("errors", out var errors))
        {
            var msg = errors[0].GetProperty("message").GetString();
            _logger.LogError("FedEx label creation failed: {Error}", msg);
            return new ShipmentLabelResult(false, Error: msg);
        }

        var output          = response.GetProperty("output")
                                      .GetProperty("transactionShipments")[0];
        var trackingNumber  = output.GetProperty("masterTrackingNumber").GetString();
        var labelUrl        = output.GetProperty("pieceResponses")[0]
                                    .GetProperty("packageDocuments")[0]
                                    .GetProperty("url").GetString();

        // download label PDF
        byte[]? labelPdf = null;
        if (!string.IsNullOrEmpty(labelUrl))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            labelPdf = await _http.GetByteArrayAsync(labelUrl, ct);
        }

        return new ShipmentLabelResult(
            Success:        true,
            TrackingNumber: trackingNumber,
            LabelPdf:       labelPdf);
    }

    public async Task<TrackingResult> TrackAsync(
        string trackingNumber, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);
        var payload = new
        {
            includeDetailedScans = true,
            trackingInfo = new[]
            {
                new { trackingNumberInfo = new { trackingNumber } }
            }
        };

        var response = await PostAsync("track/v1/trackingnumbers", payload, token, ct);

        var pkg = response
            .GetProperty("output")
            .GetProperty("completeTrackResults")[0]
            .GetProperty("trackResults")[0];

        var status    = pkg.GetProperty("latestStatusDetail")
                          .GetProperty("description").GetString() ?? "Unknown";
        var location  = pkg.TryGetProperty("latestStatusDetail", out var lsd) &&
                        lsd.TryGetProperty("scanLocation", out var loc)
            ? $"{loc.GetProperty("city").GetString()}, {loc.GetProperty("stateOrProvinceCode").GetString()}"
            : null;

        return new TrackingResult(
            Success:  true,
            Status:   status,
            Location: location);
    }

    public async Task<bool> VoidLabelAsync(
        string trackingNumber, CancellationToken ct = default)
    {
        var token   = await GetTokenAsync(ct);
        var payload = new
        {
            accountNumber = new { value = _opts.AccountNumber },
            trackingNumber
        };

        var response = await PostAsync("ship/v1/shipments/cancel", payload, token, ct);
        return !response.TryGetProperty("errors", out _);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            return _accessToken;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
        });

        var response = await _http.PostAsync("oauth/token", form, ct);
        var json     = await response.Content.ReadAsStringAsync(ct);

        using var doc  = JsonDocument.Parse(json);
        _accessToken   = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn  = doc.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiry   = DateTime.UtcNow.AddSeconds(expiresIn - 120);

        return _accessToken;
    }

    private async Task<JsonElement> PostAsync(
        string endpoint, object payload, string token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        return JsonDocument.Parse(body).RootElement;
    }

    private static object MapAddress(ShippingAddress addr) => new
    {
        contact = new { personName = addr.Name },
        address = new
        {
            streetLines          = new[] { addr.Line1, addr.Line2 }.Where(l => l != null).ToArray(),
            city                 = addr.City,
            stateOrProvinceCode  = addr.State,
            postalCode           = addr.PostalCode,
            countryCode          = addr.Country,
        }
    };

    private static string MapServiceLevel(string level) => level.ToUpperInvariant() switch
    {
        "OVERNIGHT"     => "FEDEX_OVERNIGHT",
        "2DAY"          => "FEDEX_2_DAY",
        "EXPRESS_SAVER" => "FEDEX_EXPRESS_SAVER",
        "GROUND"        => "FEDEX_GROUND",
        _ => "FEDEX_GROUND"
    };

    private static int EstimateDays(string level) => level.ToUpperInvariant() switch
    {
        "OVERNIGHT"     => 1,
        "2DAY"          => 2,
        "EXPRESS_SAVER" => 3,
        _ => 5
    };
}

public class FedExOptions
{
    public string ClientId     { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccountNumber{ get; set; } = string.Empty;
    public bool   IsSandbox    { get; set; } = true;
}
