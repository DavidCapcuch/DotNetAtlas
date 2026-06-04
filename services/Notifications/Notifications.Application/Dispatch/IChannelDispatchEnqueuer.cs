using Notifications.Domain.Channels;

namespace Notifications.Application.Dispatch;

/// <summary>
/// Enqueues a per-channel dispatch as an isolated background job. The walking skeleton (#312) backs
/// this with a fire-and-forget Hangfire enqueue. Kept as a port so the Kafka handler stays free of
/// the job scheduler and the (currently non-composable) transactional-enqueue seam has one home.
/// See ADR-0032 § 5.
/// </summary>
public interface IChannelDispatchEnqueuer
{
    /// <summary>Enqueues one background job that will dispatch <paramref name="dispatch"/> on <paramref name="channel"/>.</summary>
    void Enqueue(ChannelType channel, NotificationDispatch dispatch);
}
