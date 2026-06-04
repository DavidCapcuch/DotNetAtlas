using FluentResults;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notifications.Application.Email;
using Notifications.Infrastructure.Common.Config;

namespace Notifications.Infrastructure.Email;

/// <summary>
/// Real email transport over SMTP via MailKit → Mailpit (ADR-0032 § 6). A send failure is
/// returned as a failed <see cref="Result"/> (not an exception) so the channel dispatcher can
/// record <c>Failed</c> in the ledger and let Hangfire retry. <see cref="MockEmailGateway"/> is
/// retained for unit tests.
/// </summary>
internal sealed class SmtpEmailGateway : IEmailGateway
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailGateway> _logger;

    public SmtpEmailGateway(IOptions<SmtpOptions> options, ILogger<SmtpEmailGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.Body };

            using var client = new SmtpClient();
            // Mailpit accepts plaintext SMTP with no auth on 1025.
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.None, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            // Do not log message.To — the recipient address is PII (ADR-0011). The caller logs the
            // NotificationId/Channel context; here we only need the transport endpoint.
            _logger.LogError(
                ex,
                "SMTP send failed via {Host}:{Port}",
                _options.Host,
                _options.Port);
            return Result.Fail(new ExceptionalError("SMTP send failed.", ex));
        }
    }
}
