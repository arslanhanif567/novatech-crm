using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Payments;

// PayPal Orders API v2.
// NOTE: this provider is rarely used — most customers pay via Stripe. The PayPal
// integration was added for a single enterprise client and has seen minimal maintenance.
// TODO: refresh token caching expires every 9 hours — currently re-fetches on every call (NOVA-75)
public class PayPalPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _opts;
    private readonly ILogger<PayPalPaymentProvider> _logger;

    private string? _cachedAccessToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public PayPalPaymentProvider(
        HttpClient http,
        IOptions<PayPalOptions> opts,
        ILogger<PayPalPaymentProvider> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(opts.Value.IsSandbox
            ? "https://api-m.sandbox.paypal.com/"
            : "https://api-m.paypal.com/");
    }

    public async Task<PaymentProviderResult> ChargeAsync(
        Payment payment, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        var order = new
        {
            intent             = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    amount         = new { currency_code = payment.Currency, value = payment.Amount.ToString("F2") },
                    description    = $"NovaTech payment {payment.Id}",
                    custom_id      = payment.Id.ToString(),
                }
            }
        };

        var request  = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders")
        {
            Content = new StringContent(JsonSerializer.Serialize(order, _json),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal order creation failed for {PaymentId}: {Body}",
                payment.Id, body);
            return new PaymentProviderResult(false, ErrorMessage: body);
        }

        using var doc  = JsonDocument.Parse(body);
        var orderId    = doc.RootElement.GetProperty("id").GetString();
        var status     = doc.RootElement.GetProperty("status").GetString();

        // PayPal requires a separate capture step after the buyer approves.
        // In our flow the payment method must already be vaulted for automated capture.
        if (status != "APPROVED" && status != "COMPLETED")
        {
            return new PaymentProviderResult(false,
                ProviderPaymentId: orderId,
                ErrorMessage: $"Order status is {status} — buyer approval required.");
        }

        return new PaymentProviderResult(
            Success:           true,
            ProviderPaymentId: orderId);
    }

    public async Task<PaymentProviderResult> RefundAsync(
        Payment payment, decimal amount, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(payment.ProviderPaymentId))
            return new PaymentProviderResult(false, ErrorMessage: "No PayPal order ID.");

        var token    = await GetAccessTokenAsync(ct);
        var endpoint = $"v2/payments/captures/{payment.ProviderPaymentId}/refund";

        var body    = JsonSerializer.Serialize(new
        {
            amount = new { currency_code = payment.Currency, value = amount.ToString("F2") }
        }, _json);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response    = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new PaymentProviderResult(false, ErrorMessage: responseBody);

        using var doc  = JsonDocument.Parse(responseBody);
        var refundId   = doc.RootElement.GetProperty("id").GetString();

        return new PaymentProviderResult(Success: true, ProviderRefundId: refundId);
    }

    public Task HandleWebhookAsync(
        string payload, string signature, CancellationToken ct = default)
    {
        // TODO: implement PayPal webhook verification (NOVA-76)
        // PayPal webhook verification requires calling their /v1/notifications/verify-webhook-signature
        // endpoint which is async and rate-limited — punted for now.
        _logger.LogDebug("PayPal webhook received (signature not verified — NOVA-76)");
        return Task.CompletedTask;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        // BUG (NOVA-75): re-fetches on every call — should cache until _tokenExpiresAt
        // Left unfixed because PayPal is rarely used in prod
        if (_cachedAccessToken != null && DateTime.UtcNow < _tokenExpiresAt)
            return _cachedAccessToken;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
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

        using var doc    = JsonDocument.Parse(body);
        _cachedAccessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn      = doc.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiresAt    = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        return _cachedAccessToken;
    }
}

public class PayPalOptions
{
    public string ClientId     { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool   IsSandbox    { get; set; } = true;
}
