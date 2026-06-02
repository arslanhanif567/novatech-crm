using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NovaTechCRM.Infrastructure.Email;

// Production email sender via SendGrid Web API v3.
// We intentionally do NOT use the SendGrid .NET SDK — it pulls in 8 transitive dependencies
// and we've had DI conflicts with it twice. Raw HttpClient is fine for what we need.
public class SendGridEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly SendGridOptions _opts;
    private readonly ILogger<SendGridEmailSender> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SendGridEmailSender(
        HttpClient http,
        IOptions<SendGridOptions> opts,
        ILogger<SendGridEmailSender> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        _http.BaseAddress = new Uri("https://api.sendgrid.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var payload = BuildPayload(message);
        var json    = JsonSerializer.Serialize(payload, _json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("v3/mail/send", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("SendGrid rejected email to {To}: {Status} {Body}",
                message.To, response.StatusCode, body);
            throw new EmailDeliveryException($"SendGrid returned {(int)response.StatusCode}: {body}");
        }

        _logger.LogDebug("SendGrid: sent '{Subject}' to {To}", message.Subject, message.To);
    }

    public async Task SendBulkAsync(
        IEnumerable<EmailMessage> messages, CancellationToken ct = default)
    {
        // SendGrid supports up to 1000 personalizations per request.
        // For simplicity we batch into groups of 100. May want to increase for large campaigns.
        var batches = messages.Chunk(100);
        foreach (var batch in batches)
        {
            // build a single request with multiple personalizations
            foreach (var msg in batch)
                await SendAsync(msg, ct);
        }
    }

    private object BuildPayload(EmailMessage msg)
    {
        var payload = new
        {
            Personalizations = new[]
            {
                new
                {
                    To = new[] { new { Email = msg.To, Name = msg.ToName ?? msg.To } },
                    Subject = msg.Subject,
                }
            },
            From    = new { Email = _opts.FromAddress, Name = _opts.FromName },
            ReplyTo = msg.ReplyTo != null ? new { Email = msg.ReplyTo } : null,
            Content = new[]
            {
                msg.TextBody != null
                    ? new { Type = "text/plain", Value = msg.TextBody }
                    : null,
                new { Type = "text/html", Value = msg.HtmlBody },
            }.Where(c => c != null).ToArray(),
            Attachments = msg.Attachments.Select(a => new
            {
                Content     = Convert.ToBase64String(a.Content),
                Type        = a.ContentType,
                Filename    = a.FileName,
                Disposition = "attachment",
            }).ToArray(),
        };

        return payload;
    }
}

public class SendGridOptions
{
    public string ApiKey      { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@novatech.io";
    public string FromName    { get; set; } = "NovaTech CRM";
}

public class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message) : base(message) { }
}
