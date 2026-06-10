namespace Notifications.Application.Bell;

/// <summary>
/// Application-layer port for pushing a notification to a recipient's in-app bell. Implemented in
/// the Api layer over the SignalR hub context so Application/Infrastructure callers (the bell
/// channel dispatcher, #317) stay free of a SignalR dependency. Delivery is best-effort and
/// ephemeral: a push to a recipient with no live connection is a successful no-op (the bell has
/// no persistence or replay — ADR-0032, notifications.md § 6).
/// </summary>
public interface INotificationBroadcaster
{
    /// <summary>
    /// Pushes <paramref name="payload"/> to every live connection the recipient currently holds
    /// (the recipient's per-user group, keyed by their <c>RecipientUserId</c>).
    /// </summary>
    Task PushToUserAsync(Guid recipientUserId, BellNotification payload, CancellationToken ct);
}
