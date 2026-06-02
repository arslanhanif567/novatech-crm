using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;

namespace NovaTechCRM.Infrastructure.Sms;

public interface ISmsSender
{
    Task SendAsync(string toPhoneNumber, string message, CancellationToken ct = default);
}

// Twilio REST API — we use raw HTTP rather than the Twilio SDK because the SDK
// ships with a bundled version of Newtonsoft.Json that clashes with our System.Text.Json setup.
// See: https://github.com/twilio/twilio-csharp/issues/612 (still open as of writing)
public class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _http;
    private readonly TwilioOptions _opts;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(
        HttpClient http,
        IOptions<TwilioOptions> opts,
        ILogger<TwilioSmsSender> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_opts.AccountSid}:{_opts.AuthToken}"));

        _http.BaseAddress = new Uri($"https://api.twilio.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task SendAsync(
        string toPhoneNumber, string message, CancellationToken ct = default)
    {
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = _opts.FromNumber,
            ["To"]   = toPhoneNumber,
            ["Body"] = message,
        });

        var url      = $"2010-04-01/Accounts/{_opts.AccountSid}/Messages.json";
        var response = await _http.PostAsync(url, formData, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Twilio failed for {To}: {Status} {Body}",
                toPhoneNumber, response.StatusCode, body);

            // don't throw — SMS failure is non-fatal for most flows
            return;
        }

        _logger.LogDebug("SMS sent to {To}", toPhoneNumber);
    }
}

public class TwilioOptions
{
    public string AccountSid  { get; set; } = string.Empty;
    public string AuthToken   { get; set; } = string.Empty;
    public string FromNumber  { get; set; } = string.Empty;
}
