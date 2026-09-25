using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// The fixture's <see cref="IChannelDispatchEnqueuer"/> in place of the Hangfire one: records what the
/// fan-out handler enqueues and, on <see cref="DrainAsync"/>, runs each entry through the <b>real</b>
/// job classes (one DI scope per entry, mirroring Hangfire's per-job scope) — so only Hangfire's queue
/// mechanics are replaced (the test host never starts the Hangfire server).
/// </summary>
/// <remarks>
/// <c>executeAt</c> is recorded but not honoured: entries run immediately on drain, so a quiet-hours
/// deferral does not delay a test.
/// </remarks>
internal sealed class RecordingChannelDispatchEnqueuer : IChannelDispatchEnqueuer
{
    private readonly ConcurrentQueue<(ChannelType Channel, NotificationDispatch Dispatch, DateTimeOffset ExecuteAt)> _pending = new();

    public void Enqueue(ChannelType channel, NotificationDispatch dispatch, DateTimeOffset executeAt)
    {
        _pending.Enqueue((channel, dispatch, executeAt));
    }

    /// <summary>Forgets every pending entry; the fixture calls this between tests.</summary>
    public void Clear()
    {
        _pending.Clear();
    }

    /// <summary>Runs every pending entry through its channel's real Hangfire job class.</summary>
    public async Task DrainAsync(IServiceProvider rootProvider, CancellationToken ct)
    {
        while (_pending.TryDequeue(out var entry))
        {
            await using var scope = rootProvider.CreateAsyncScope();
            await DispatchJobs.RunAsync(scope.ServiceProvider, entry.Channel, entry.Dispatch, ct);
        }
    }
}
