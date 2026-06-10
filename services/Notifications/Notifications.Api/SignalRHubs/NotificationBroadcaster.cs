using Microsoft.AspNetCore.SignalR;
using Notifications.Application.Bell;
using Notifications.Infrastructure.Common.Observability.Tracing;

namespace Notifications.Api.SignalRHubs;

/// <summary>
/// <see cref="INotificationBroadcaster"/> over the bell hub context: pushes a payload to the
/// recipient's per-user group (keyed by RecipientUserId). A group with no live connections is a
/// successful no-op — the bell is ephemeral, with no persistence or replay (ADR-0032).
/// </summary>
internal sealed class NotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<NotificationHub, INotificationClientContract> _hubContext;
    private readonly ILogger<NotificationBroadcaster> _logger;

    public NotificationBroadcaster(
        IHubContext<NotificationHub, INotificationClientContract> hubContext,
        ILogger<NotificationBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PushToUserAsync(Guid recipientUserId, BellNotification payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var groupName = recipientUserId.ToString();

        using var activity = NotificationsActivitySource.StartActivity(nameof(PushToUserAsync));
        activity?.SetTag("notifications.bell.recipient_group", groupName);

        _logger.LogInformation("Pushing bell notification to user group {RecipientUserId}.", recipientUserId);

        // SignalR client-method invocations are fire-and-forget over the live connections and take
        // no CancellationToken; ct cancels only any caller-side orchestration before this point.
        await _hubContext.Clients
            .Group(groupName)
            .ReceiveNotification(payload);
    }
}
