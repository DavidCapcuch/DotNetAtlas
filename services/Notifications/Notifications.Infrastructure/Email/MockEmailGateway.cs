using FluentResults;
using Microsoft.Extensions.Logging;
using Notifications.Application.Email;

namespace Notifications.Infrastructure.Email;

/// <summary>Logs the email and returns success. Default DI registration in dev/test.</summary>
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
        _logger.LogInformation(
            "[MOCK EMAIL] to={ToUserId} subject='{Subject}' body-len={BodyLen} at={At:O}",
            message.ToUserId, message.Subject, message.Body.Length, _clock.GetUtcNow());
        return Task.FromResult(Result.Ok());
    }
}
