using FluentResults;

namespace Notifications.Application.Email;

/// <summary>Abstraction over the underlying email transport. Mock in Phase 1; real
/// gateway (SendGrid/SMTP) is a Phase-2 follow-up.</summary>
public interface IEmailGateway
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct);
}
