using Notifications.Domain.Channels;

namespace Notifications.Application.Dispatch;

/// <summary>
/// Enqueues a per-channel dispatch as an isolated background job, at the instant the handler
/// computed for that channel (now, or a quiet-hours deferral — ADR-0032 § 3/§ 5). Backed by
/// Hangfire (#312/#315). Kept as a port so the Kafka handler stays free of the job scheduler and
/// the (currently non-composable) transactional-enqueue seam has one home.
/// </summary>
public interface IChannelDispatchEnqueuer
{
    /// <summary>
    /// Enqueues one background job that will dispatch <paramref name="dispatch"/> on
    /// <paramref name="channel"/> at <paramref name="executeAt"/> (immediately when that instant
    /// is not in the future).
    /// </summary>
    void Enqueue(ChannelType channel, NotificationDispatch dispatch, DateTimeOffset executeAt);
}
