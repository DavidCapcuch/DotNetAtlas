using KafkaFlow;
using Microsoft.Extensions.Logging;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;

namespace Notifications.Infrastructure.NotifyUser;

/// <summary>
/// Consumes <c>NotifyUserCommand</c> from <c>notifications.notify-commands</c>. Runs inside the
/// platform <c>InboxMiddleware</c> transaction (dedup on the <c>message.id</c> header). Resolves the
/// fixed <c>[Email]</c> channel set (the walking skeleton's only channel) and enqueues one
/// background dispatch job per channel. Any enqueue failure throws → the inbox row rolls back →
/// Kafka re-drives the whole fan-out (duplicate jobs absorbed by the per-channel ledger). ADR-0031/0032.
/// </summary>
public sealed class NotifyUserCommandKafkaHandler : IMessageHandler<NotifyUserCommand>
{
    // Fixed channel set for #312. Preference-driven resolution (enabled ∩ template channels)
    // lands with the user_preferences/templates tables in #313/#314.
    private static readonly ChannelType[] ResolvedChannels = [ChannelType.Email];

    private readonly IChannelDispatchEnqueuer _enqueuer;
    private readonly ILogger<NotifyUserCommandKafkaHandler> _logger;

    public NotifyUserCommandKafkaHandler(
        IChannelDispatchEnqueuer enqueuer,
        ILogger<NotifyUserCommandKafkaHandler> logger)
    {
        _enqueuer = enqueuer;
        _logger = logger;
    }

    public Task Handle(IMessageContext context, NotifyUserCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cmd);

        var dispatch = new NotificationDispatch
        {
            NotificationId = cmd.NotificationId,
            RecipientUserId = cmd.RecipientUserId,
            TemplateKey = cmd.TemplateKey,
            Payload = new Dictionary<string, string>(cmd.Payload),
        };

        foreach (var channel in ResolvedChannels)
        {
            _enqueuer.Enqueue(channel, dispatch);
        }

        _logger.LogInformation(
            "Enqueued {ChannelCount} channel dispatch job(s) for NotificationId={NotificationId}, TemplateKey={TemplateKey}",
            ResolvedChannels.Length,
            cmd.NotificationId,
            cmd.TemplateKey);

        return Task.CompletedTask;
    }
}
