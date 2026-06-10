using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Infrastructure.Dispatch;

namespace Notifications.FunctionalTests.Common;

/// <summary>
/// Test-double <see cref="IChannelDispatchEnqueuer"/> for end-to-end dispatch tests: records what
/// the fan-out handler enqueues and, on <see cref="DrainAsync"/>, executes each entry through the
/// <b>real</b> job classes (one DI scope per entry, mirroring Hangfire's per-job scope) — so a
/// functional test covers handler → job → keyed dispatcher → broadcaster → hub → client with only
/// Hangfire's queue mechanics replaced (the test host never starts the Hangfire server).
/// </summary>
/// <remarks>
/// The durable/ephemeral job-type choice below <i>mirrors</i> <c>HangfireChannelDispatchEnqueuer</c>'s
/// <see cref="ChannelType.IsDurable"/> branch rather than exercising it — that branch is
/// unit-covered. <c>executeAt</c> is recorded but not honoured: entries execute immediately on
/// drain, so a quiet-hours deferral does not delay a test.
/// </remarks>
internal sealed class RecordingChannelDispatchEnqueuer : IChannelDispatchEnqueuer
{
    private readonly ConcurrentQueue<(ChannelType Channel, NotificationDispatch Dispatch, DateTimeOffset ExecuteAt)> _pending = new();
    private readonly ConcurrentQueue<ChannelType> _history = new();

    /// <summary>Every channel ever enqueued, in order — unlike the pending queue, draining does not consume it.</summary>
    public IReadOnlyList<ChannelType> RecordedChannels => [.. _history];

    public void Enqueue(ChannelType channel, NotificationDispatch dispatch, DateTimeOffset executeAt)
    {
        _pending.Enqueue((channel, dispatch, executeAt));
        _history.Enqueue(channel);
    }

    /// <summary>Executes every pending entry through its channel's real Hangfire job class.</summary>
    public async Task DrainAsync(IServiceProvider rootProvider, CancellationToken ct)
    {
        while (_pending.TryDequeue(out var entry))
        {
            await using var scope = rootProvider.CreateAsyncScope();
            if (entry.Channel.IsDurable)
            {
                await scope.ServiceProvider.GetRequiredService<NotificationDispatchJob>()
                    .ExecuteAsync(entry.Channel.Name, entry.Dispatch, ct);
            }
            else
            {
                await scope.ServiceProvider.GetRequiredService<EphemeralNotificationDispatchJob>()
                    .ExecuteAsync(entry.Channel.Name, entry.Dispatch, ct);
            }
        }
    }
}
