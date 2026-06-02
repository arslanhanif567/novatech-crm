using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Payments;

// Braintree / PayPal's second gateway — used for customers on legacy payment plans.
// New customers go through Stripe. This exists because we can't migrate legacy vaulted cards.
// DO NOT onboard new customers here unless you have a very good reason.
public class BraintreePaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly BraintreeOptions _opts;
    private readonly ILogger<BraintreePaymentProvider> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BraintreePaymentProvider(
        HttpClient http,
        IOptions<BraintreeOptions> opts,
        ILogger<BraintreePaymentProvider> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        var baseUrl = opts.Value.IsSandbox
            ? "https://payments.sandbox.braintree-api.com/graphql"
            : "https://payments.braintree-api.com/graphql";

        _http.BaseAddress = new Uri(baseUrl);

        var creds = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{opts.Value.PublicKey}:{opts.Value.PrivateKey}"));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", creds);
        _http.DefaultRequestHeaders.Add("Braintree-Version", "2019-01-01");
    }

    public async Task<PaymentProviderResult> ChargeAsync(
        Payment payment, CancellationToken ct = default)
    {
        var query = """
            mutation ChargeCreditCard($input: ChargeCreditCardInput!) {
              chargeCreditCard(input: $input) {
                transaction {
                  id
                  status
                  paymentMethodDetails {
                    ... on CreditCardDetails {
                      last4
                      cardType
                    }
                  }
                }
              }
            }
            """;

        var variables = new
        {
            input = new
            {
                paymentMethodId = payment.PaymentMethodId?.ToString() ?? "",
                transaction = new
                {
                    amount       = payment.Amount.ToString("F2"),
                    orderId      = payment.Id.ToString(),
                    purchaseOrderNumber = payment.Id.ToString()[..8],
                }
            }
        };

        var result = await ExecuteGraphQlAsync(query, variables, ct);

        if (result.TryGetProperty("errors", out var errors))
        {
            var msg = errors[0].GetProperty("message").GetString();
            _logger.LogWarning("Braintree charge failed for {PaymentId}: {Message}",
                payment.Id, msg);
            return new PaymentProviderResult(false, ErrorMessage: msg);
        }

        var tx      = result.GetProperty("data")
                           .GetProperty("chargeCreditCard")
                           .GetProperty("transaction");
        var txId    = tx.GetProperty("id").GetString();
        var status  = tx.GetProperty("status").GetString();

        string? last4 = null;
        string? brand = null;
        if (tx.TryGetProperty("paymentMethodDetails", out var pmd))
        {
            pmd.TryGetProperty("last4", out var l4Elem);
            pmd.TryGetProperty("cardType", out var brandElem);
            last4 = l4Elem.GetString();
            brand = brandElem.GetString();
        }

        bool success = status is "SUBMITTED_FOR_SETTLEMENT" or "SETTLING" or "SETTLED";

        return new PaymentProviderResult(
            Success:           success,
            ProviderPaymentId: txId,
            ProviderChargeId:  txId,
            CardLast4:         last4,
            CardBrand:         brand,
            ErrorMessage:      success ? null : $"Unexpected status: {status}");
    }

    public async Task<PaymentProviderResult> RefundAsync(
        Payment payment, decimal amount, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(payment.ProviderChargeId))
            return new PaymentProviderResult(false, ErrorMessage: "No Braintree transaction ID.");

        var query = """
            mutation RefundTransaction($input: RefundTransactionInput!) {
              refundTransaction(input: $input) {
                refund {
                  id
                  status
                }
              }
            }
            """;

        var variables = new
        {
            input = new
            {
                transactionId = payment.ProviderChargeId,
                refund = new { amount = amount.ToString("F2") }
            }
        };

        var result = await ExecuteGraphQlAsync(query, variables, ct);

        if (result.TryGetProperty("errors", out var errors))
            return new PaymentProviderResult(false,
                ErrorMessage: errors[0].GetProperty("message").GetString());

        var refundId = result.GetProperty("data")
                             .GetProperty("refundTransaction")
                             .GetProperty("refund")
                             .GetProperty("id")
                             .GetString();

        return new PaymentProviderResult(Success: true, ProviderRefundId: refundId);
    }

    public Task HandleWebhookAsync(
        string payload, string signature, CancellationToken ct = default)
    {
        // Braintree uses XML webhooks with bt_signature + bt_payload params.
        // We don't parse these — Braintree webhooks are informational only in our setup.
        _logger.LogDebug("Braintree webhook received (not processed)");
        return Task.CompletedTask;
    }

    private async Task<JsonElement> ExecuteGraphQlAsync(
        string query, object variables, CancellationToken ct)
    {
        var body    = JsonSerializer.Serialize(new { query, variables }, _json);
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("", content, ct);
        var json     = await response.Content.ReadAsStringAsync(ct);

        return JsonDocument.Parse(json).RootElement;
    }
}

public class BraintreeOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string PublicKey  { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public bool   IsSandbox  { get; set; } = true;
}
