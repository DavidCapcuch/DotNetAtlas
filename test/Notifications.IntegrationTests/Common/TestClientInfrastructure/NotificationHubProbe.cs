using Notifications.Application.Bell;
using Platform.Test.Framework.Common;

namespace Notifications.IntegrationTests.Common.TestClientInfrastructure;

/// <summary>
/// Deterministic observation of what a bell client received. SignalR runs the hub's
/// <c>OnConnectedAsync</c> — the per-user group auto-join — shortly <i>after</i> the client's
/// <c>StartAsync</c> returns, so the first push can outrun the join; beyond that, one connection's
/// messages arrive in send order, which lets a sentinel stand in for every timing window.
/// </summary>
internal static class NotificationHubProbe
{
    private static readonly TimeSpan PerAttempt = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan JoinDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SentinelDeadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Pushes <paramref name="payload"/> to the user's group until the client observes a delivery,
    /// retrying across the join race. Throws if none lands within the deadline.
    /// </summary>
    public static async Task<BellNotification> PushUntilReceivedAsync(
        INotificationBroadcaster broadcaster,
        Guid userId,
        NotificationHubTestClient client,
        BellNotification payload,
        CancellationToken ct)
    {
        BellNotification? received = null;
        await Eventually.UntilAsync(
            async token =>
            {
                await broadcaster.PushToUserAsync(userId, payload, token);
                received = await client.ConsumeOne(PerAttempt, token);
                return received is not null;
            },
            JoinDeadline,
            $"user {userId}'s bell client to receive a push",
            ct);

        return received!;
    }

    /// <summary>
    /// Proves the client has joined its user group before a test asserts what it received —
    /// without this, "received nothing" passes on a client that simply had not joined yet — then
    /// discards any duplicate probes still in flight so none is mistaken for a real push.
    /// </summary>
    public static async Task ProveGroupJoinedAsync(
        INotificationBroadcaster broadcaster,
        Guid userId,
        NotificationHubTestClient client,
        CancellationToken ct)
    {
        await PushUntilReceivedAsync(broadcaster, userId, client, new BellNotification("group-join-probe"), ct);
        _ = await ReceivedBeforeSentinelAsync(broadcaster, userId, client, ct);
    }

    /// <summary>
    /// Pushes a unique sentinel and returns everything the client received before it. Anything
    /// already pushed to this user arrives first, so an empty result means nothing was pushed and a
    /// single-element result means exactly one push — with no wall-clock window to guess at.
    /// </summary>
    public static async Task<IReadOnlyList<BellNotification>> ReceivedBeforeSentinelAsync(
        INotificationBroadcaster broadcaster,
        Guid userId,
        NotificationHubTestClient client,
        CancellationToken ct)
    {
        var sentinel = new BellNotification($"sentinel-{Guid.CreateVersion7()}");
        await broadcaster.PushToUserAsync(userId, sentinel, ct);

        var receivedBefore = new List<BellNotification>();
        while (true)
        {
            var next = await client.ConsumeOne(SentinelDeadline, ct)
                ?? throw new InvalidOperationException(
                    $"The sentinel pushed to user {userId} never arrived; the client is not in that user's group.");

            if (next == sentinel)
            {
                return receivedBefore;
            }

            receivedBefore.Add(next);
        }
    }
}
