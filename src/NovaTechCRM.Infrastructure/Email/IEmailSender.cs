namespace NovaTechCRM.Infrastructure.Email;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
    Task SendBulkAsync(IEnumerable<EmailMessage> messages, CancellationToken ct = default);
}

public class EmailMessage
{
    public string To           { get; init; } = string.Empty;
    public string? ToName      { get; init; }
    public string Subject      { get; init; } = string.Empty;
    public string HtmlBody     { get; init; } = string.Empty;
    public string? TextBody    { get; init; }
    public string? ReplyTo     { get; init; }
    public List<EmailAttachment> Attachments { get; init; } = new();
}

public class EmailAttachment
{
    public string FileName    { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] Content     { get; init; } = Array.Empty<byte>();
}
