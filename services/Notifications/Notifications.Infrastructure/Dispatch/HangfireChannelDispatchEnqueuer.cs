using Hangfire;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Hangfire-backed <see cref="IChannelDispatchEnqueuer"/> — one fire-and-forget job per channel.
/// ADR-0032 § 5: enlisting this enqueue in the platform <c>InboxMiddleware</c>'s EF
/// <c>DbTransaction</c> does not compose (Hangfire.PostgreSql only enlists a System.Transactions
/// <c>TransactionScope</c>, not an existing EF <c>DbTransaction</c>), so the enqueue is at-least-once:
/// a crash before the inbox row commits re-drives the whole fan-out, and the
/// <c>(NotificationId, Channel)</c> ledger collapses the duplicate.
/// </summary>
internal sealed class HangfireChannelDispatchEnqueuer : IChannelDispatchEnqueuer
{
    private readonly IBackgroundJobClientV2 _backgroundJobs;

    public HangfireChannelDispatchEnqueuer(IBackgroundJobClientV2 backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public void Enqueue(ChannelType channel, NotificationDispatch dispatch)
    {
        _backgroundJobs.Enqueue<NotificationDispatchJob>(
            job => job.ExecuteAsync(channel.Name, dispatch, CancellationToken.None));
    }
}
