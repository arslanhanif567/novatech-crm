using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NovaTechCRM.Infrastructure.Email;

// Legacy SMTP sender — kept for dev/staging environments.
// Production uses SendGridEmailSender. Do not remove this class — the integration
// test suite spins up a local SMTP server (Papercut) and uses this.
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _opts;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> opts, ILogger<SmtpEmailSender> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        using var client = BuildClient();
        using var mail   = BuildMailMessage(message);

        await client.SendMailAsync(mail, ct);

        _logger.LogDebug("SMTP: sent '{Subject}' to {To}", message.Subject, message.To);
    }

    public async Task SendBulkAsync(
        IEnumerable<EmailMessage> messages, CancellationToken ct = default)
    {
        // SMTP has no batch API — send one at a time
        // TODO: consider switching bulk sends to SendGrid for performance (NOVA-60)
        foreach (var msg in messages)
            await SendAsync(msg, ct);
    }

    private SmtpClient BuildClient()
    {
        var client = new SmtpClient(_opts.Host, _opts.Port)
        {
            EnableSsl            = _opts.EnableSsl,
            DeliveryMethod       = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };

        if (!string.IsNullOrEmpty(_opts.Username))
            client.Credentials = new NetworkCredential(_opts.Username, _opts.Password);

        return client;
    }

    private MailMessage BuildMailMessage(EmailMessage msg)
    {
        var mail = new MailMessage
        {
            From       = new MailAddress(_opts.FromAddress, _opts.FromName),
            Subject    = msg.Subject,
            Body       = msg.HtmlBody,
            IsBodyHtml = true,
        };

        mail.To.Add(msg.ToName != null
            ? new MailAddress(msg.To, msg.ToName)
            : new MailAddress(msg.To));

        if (!string.IsNullOrEmpty(msg.ReplyTo))
            mail.ReplyToList.Add(new MailAddress(msg.ReplyTo));

        foreach (var att in msg.Attachments)
            mail.Attachments.Add(new Attachment(
                new MemoryStream(att.Content), att.FileName, att.ContentType));

        return mail;
    }
}

public class SmtpOptions
{
    public string Host        { get; set; } = "localhost";
    public int    Port        { get; set; } = 25;
    public bool   EnableSsl   { get; set; } = false;
    public string Username    { get; set; } = string.Empty;
    public string Password    { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@novatech.io";
    public string FromName    { get; set; } = "NovaTech CRM";
}
