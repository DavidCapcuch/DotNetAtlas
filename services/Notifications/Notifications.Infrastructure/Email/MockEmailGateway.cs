using FluentResults;
using Microsoft.Extensions.Logging;
using Notifications.Application.Email;

namespace Notifications.Infrastructure.Email;

/// <summary>Logs the email (without the recipient address) and returns success. Used directly by unit
/// tests; production/dev DI registers <see cref="SmtpEmailGateway"/> (→ Mailpit).</summary>
internal sealed class MockEmailGateway : IEmailGateway
{
    private readonly ILogger<MockEmailGateway> _logger;
    private readonly TimeProvider _clock;

    public MockEmailGateway(ILogger<MockEmailGateway> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public Task<Result> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Recipient address omitted — it is PII (ADR-0011).
        _logger.LogInformation(
            "[MOCK EMAIL] subject='{Subject}' body-len={BodyLen} at={At:O}",
            message.Subject, message.Body.Length, _clock.GetUtcNow());
        return Task.FromResult(Result.Ok());
    }
}
