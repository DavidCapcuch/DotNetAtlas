using Hangfire;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Hangfire-backed <see cref="IChannelDispatchEnqueuer"/> — one job per channel: a future
/// <c>executeAt</c> is a <c>Schedule</c> (quiet-hours deferral, ADR-0032 § 3), anything else a
/// fire-and-forget <c>Enqueue</c> — scheduled jobs only fire on the schedule-poll tick, so routing
/// immediate dispatches through <c>Schedule(now)</c> would tax every email/bell with poll latency.
/// ADR-0032 § 5: enlisting this enqueue in the platform <c>InboxMiddleware</c>'s EF
/// <c>DbTransaction</c> does not compose (Hangfire.PostgreSql only enlists a System.Transactions
/// <c>TransactionScope</c>, not an existing EF <c>DbTransaction</c>), so the enqueue is at-least-once:
/// a crash before the inbox row commits re-drives the whole fan-out, and the
/// <c>(NotificationId, Channel)</c> ledger collapses the duplicate.
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
        if (executeAt > _clock.GetUtcNow())
        {
            _backgroundJobs.Schedule<NotificationDispatchJob>(
                job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None),
                executeAt);
        }
        else
        {
            _backgroundJobs.Enqueue<NotificationDispatchJob>(
                job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None));
        }
    }
}
