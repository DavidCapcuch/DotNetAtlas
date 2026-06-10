using Hangfire;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Hangfire-backed <see cref="IChannelDispatchEnqueuer"/> — one job per channel: a future
/// <c>executeAt</c> is a <c>Schedule</c> (quiet-hours deferral, ADR-0032 § 3), anything else a
/// fire-and-forget <c>Enqueue</c> — scheduled jobs only fire on the schedule-poll tick, so routing
/// immediate dispatches through <c>Schedule(now)</c> would tax every email/bell with poll latency.
/// The job type follows <see cref="ChannelType.IsDurable"/>: durable channels ride the full-retry
/// <see cref="NotificationDispatchJob"/>, ephemeral ones the minimal-retry
/// <see cref="EphemeralNotificationDispatchJob"/>.
/// ADR-0032 § 5: enlisting this enqueue in the platform <c>InboxMiddleware</c>'s EF
/// <c>DbTransaction</c> does not compose (Hangfire.PostgreSql only enlists a System.Transactions
/// <c>TransactionScope</c>, not an existing EF <c>DbTransaction</c>), so the enqueue is at-least-once:
/// a crash before the inbox row commits re-drives the whole fan-out — the
/// <c>(NotificationId, Channel)</c> ledger collapses the duplicate on durable channels, while an
/// ephemeral channel may double-push (no ledger; accepted best-effort behaviour, ADR-0032 § 2).
/// </summary>
internal sealed class HangfireChannelDispatchEnqueuer : IChannelDispatchEnqueuer
{
    private readonly IBackgroundJobClientV2 _backgroundJobs;
    private readonly TimeProvider _clock;

    public HangfireChannelDispatchEnqueuer(IBackgroundJobClientV2 backgroundJobs, TimeProvider clock)
    {
        _backgroundJobs = backgroundJobs;
        _clock = clock;
    }

    public void Enqueue(ChannelType channel, NotificationDispatch dispatch, DateTimeOffset executeAt)
    {
        // Hangfire's Enqueue/Schedule are generic over the job type, so the durable/ephemeral split
        // is an explicit branch rather than a runtime type parameter.
        if (executeAt > _clock.GetUtcNow())
        {
            if (channel.IsDurable)
            {
                _backgroundJobs.Schedule<NotificationDispatchJob>(
                    job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None),
                    executeAt);
            }
            else
            {
                _backgroundJobs.Schedule<EphemeralNotificationDispatchJob>(
                    job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None),
                    executeAt);
            }
        }
        else
        {
            if (channel.IsDurable)
            {
                _backgroundJobs.Enqueue<NotificationDispatchJob>(
                    job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None));
            }
            else
            {
                _backgroundJobs.Enqueue<EphemeralNotificationDispatchJob>(
                    job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None));
            }
        }
    }
}
