using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Payments;

// Stripe payment provider — uses Stripe's REST API directly.
// We evaluated Stripe.net SDK but the 45MB package size was rejected by the infra team.
// The surface area we use is small enough to not need the full SDK.
public class StripePaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly StripeOptions _opts;
    private readonly ILogger<StripePaymentProvider> _logger;

    public StripePaymentProvider(
        HttpClient http,
        IOptions<StripeOptions> opts,
        ILogger<StripePaymentProvider> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        _http.BaseAddress = new Uri("https://api.stripe.com/v1/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _opts.SecretKey);
    }

    public async Task<PaymentProviderResult> ChargeAsync(
        Payment payment, CancellationToken ct = default)
    {
        var amountCents = (long)(payment.Amount * 100);

        var form = new Dictionary<string, string>
        {
            ["amount"]      = amountCents.ToString(),
            ["currency"]    = payment.Currency.ToLowerInvariant(),
            ["description"] = $"NovaTech order payment — {payment.Id}",
        };

        if (payment.PaymentMethodId.HasValue)
            form["payment_method"] = payment.PaymentMethodId.Value.ToString();

        if (!string.IsNullOrEmpty(_opts.StatementDescriptor))
            form["statement_descriptor"] = _opts.StatementDescriptor;

        var response = await PostFormAsync("payment_intents", form, ct);

        if (response.TryGetProperty("error", out var err))
        {
            _logger.LogWarning("Stripe charge declined for payment {Id}: {Code} {Message}",
                payment.Id,
                err.GetProperty("code").GetString(),
                err.GetProperty("message").GetString());

            return new PaymentProviderResult(
                Success:      false,
                ErrorCode:    err.GetProperty("code").GetString(),
                ErrorMessage: err.GetProperty("message").GetString());
        }

        var chargeId = response.GetProperty("latest_charge").GetString();
        var piId     = response.GetProperty("id").GetString();

        // fetch charge to get card details
        string? cardLast4 = null;
        string? cardBrand = null;
        if (!string.IsNullOrEmpty(chargeId))
        {
            var charge = await GetAsync($"charges/{chargeId}", ct);
            if (charge.TryGetProperty("payment_method_details", out var pmd) &&
                pmd.TryGetProperty("card", out var card))
            {
                cardLast4 = card.GetProperty("last4").GetString();
                cardBrand = card.GetProperty("brand").GetString();
            }
        }

        return new PaymentProviderResult(
            Success:           true,
            ProviderPaymentId: piId,
            ProviderChargeId:  chargeId,
            CardLast4:         cardLast4,
            CardBrand:         cardBrand);
    }

    public async Task<PaymentProviderResult> RefundAsync(
        Payment payment, decimal amount, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(payment.ProviderChargeId))
            return new PaymentProviderResult(false, ErrorMessage: "No charge ID on record.");

        var form = new Dictionary<string, string>
        {
            ["charge"] = payment.ProviderChargeId,
            ["amount"] = ((long)(amount * 100)).ToString(),
        };

        var response = await PostFormAsync("refunds", form, ct);

        if (response.TryGetProperty("error", out var err))
        {
            return new PaymentProviderResult(
                Success:      false,
                ErrorCode:    err.GetProperty("code").GetString(),
                ErrorMessage: err.GetProperty("message").GetString());
        }

        return new PaymentProviderResult(
            Success:           true,
            ProviderRefundId:  response.GetProperty("id").GetString());
    }

    public async Task HandleWebhookAsync(
        string payload, string signature, CancellationToken ct = default)
    {
        // signature verification — HMAC-SHA256 of the payload with the webhook secret
        if (!VerifyStripeSignature(payload, signature, _opts.WebhookSecret))
        {
            _logger.LogWarning("Stripe webhook signature verification failed");
            throw new InvalidOperationException("Webhook signature mismatch.");
        }

        using var doc   = JsonDocument.Parse(payload);
        var eventType   = doc.RootElement.GetProperty("type").GetString();

        _logger.LogInformation("Stripe webhook: {EventType}", eventType);

        // TODO: handle charge.dispute.created, charge.refund.updated etc. (NOVA-68)
        switch (eventType)
        {
            case "payment_intent.succeeded":
            case "payment_intent.payment_failed":
            case "charge.refunded":
                // These are handled optimistically via our own API response.
                // Webhooks are a safety net for async state changes.
                break;
        }
    }

    private static bool VerifyStripeSignature(
        string payload, string sigHeader, string secret)
    {
        // Stripe sends: t=timestamp,v1=signature
        var parts     = sigHeader.Split(',');
        var timestamp = parts.FirstOrDefault(p => p.StartsWith("t="))?.Substring(2) ?? "";
        var v1Sig     = parts.FirstOrDefault(p => p.StartsWith("v1="))?.Substring(3) ?? "";

        var signedPayload = $"{timestamp}.{payload}";
        var key           = Encoding.UTF8.GetBytes(secret);
        var data          = Encoding.UTF8.GetBytes(signedPayload);

        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var hash       = hmac.ComputeHash(data);
        var computed   = Convert.ToHexString(hash).ToLowerInvariant();

        return computed == v1Sig;
    }

    private async Task<JsonElement> PostFormAsync(
        string endpoint, Dictionary<string, string> form, CancellationToken ct)
    {
        var content  = new FormUrlEncodedContent(form);
        var response = await _http.PostAsync(endpoint, content, ct);
        var json     = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task<JsonElement> GetAsync(string endpoint, CancellationToken ct)
    {
        var response = await _http.GetAsync(endpoint, ct);
        var json     = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement;
    }
}

public class StripeOptions
{
    public string SecretKey          { get; set; } = string.Empty;
    public string PublishableKey     { get; set; } = string.Empty;
    public string WebhookSecret      { get; set; } = string.Empty;
    public string StatementDescriptor{ get; set; } = "NOVATECH";
}
