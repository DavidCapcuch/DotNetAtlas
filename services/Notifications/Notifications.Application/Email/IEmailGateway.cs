using FluentResults;

namespace Notifications.Application.Email;

/// <summary>Abstraction over the underlying email transport. Returns a failed <see cref="Result"/>
/// (not an exception) on a send failure, so the channel dispatcher can record <c>Failed</c> in the
/// ledger and let Hangfire retry. Live impl is <c>SmtpEmailGateway</c> (MailKit → Mailpit, ADR-0032 § 6);
/// <c>MockEmailGateway</c> is retained for unit tests.</summary>
public interface IEmailGateway
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct);
}
