using Notifications.Domain.Channels;

namespace Notifications.Application.Dispatch;

/// <summary>
/// Sends one notification on a single channel. Registered in Keyed DI by <see cref="ChannelType"/>
/// and invoked from an isolated Hangfire job so channels retry independently. Durable channels
/// guard the send with the (<c>NotificationId</c>, <c>Channel</c>) ledger and emit a delivery
/// event. The walking skeleton (#312) wires only the email dispatcher. See ADR-0032.
/// </summary>
public interface IChannelDispatcher
{
    /// <summary>The channel this dispatcher serves (matches its Keyed-DI key).</summary>
    ChannelType Channel { get; }

    /// <summary>Renders and sends the notification on this channel, recording the outcome.</summary>
    Task DispatchAsync(NotificationDispatch dispatch, CancellationToken ct);
}
