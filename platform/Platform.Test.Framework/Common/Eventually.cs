using System.Diagnostics;

namespace Platform.Test.Framework.Common;

/// <summary>
/// Polls a condition until it holds or a deadline expires.
/// </summary>
/// <remarks>
/// For state that arrives asynchronously with no completion signal to await — a row a background
/// worker will delete, a cache a consumer will evict. A fixed delay is either flaky or slow, and a
/// poll with no deadline turns a failure into a hung run that produces no stack trace at all. This
/// throws rather than returning <c>false</c> so the failure names what was awaited instead of
/// surfacing as a bare assertion on a boolean.
/// </remarks>
public static class Eventually
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Cap on the probe made after the deadline. Generous enough for one query against a loaded
    /// dependency, short enough that a wedged one still fails with a stack trace.
    /// </summary>
    private static readonly TimeSpan GraceProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Polls <paramref name="probe"/> until it returns <c>true</c>.
    /// </summary>
    /// <param name="probe">
    /// The condition to poll. Every call is handed a token that expires with the wait, and must pass
    /// it to whatever it awaits — a probe that blocks on a query with no timeout of its own is the
    /// one way this can still hang, and the one case where the overrun below is not bounded.
    /// </param>
    /// <param name="timeout">
    /// How long to keep polling. One further probe runs after this expires, so a condition that
    /// first holds during the last poll interval still passes, and a wait that fails can overrun by
    /// that probe's budget — the smaller of <paramref name="timeout"/> and five seconds. The
    /// timeout message reports the time actually elapsed, not this value.
    /// </param>
    /// <param name="because">
    /// What is being awaited, phrased to complete "waiting for ..."; becomes the timeout message.
    /// </param>
    /// <param name="ct">Cancellation token. Cancelling it propagates rather than becoming a timeout.</param>
    /// <exception cref="EventuallyTimeoutException">Thrown when the condition never held.</exception>
    public static async Task UntilAsync(
        Func<CancellationToken, Task<bool>> probe,
        TimeSpan timeout,
        string because,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(because);

        var stopwatch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            while (true)
            {
                if (await probe(deadline.Token))
                {
                    return;
                }

                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // It is the poll delay, not the condition, that trips the deadline — so a condition
            // that became true during that delay would otherwise fail a run that had succeeded.
            var (held, fault) = await ProbeOnceMoreAsync(probe, timeout, ct);
            if (held)
            {
                return;
            }

            throw new EventuallyTimeoutException(
                $"Timed out after {stopwatch.Elapsed.TotalSeconds:0.#}s waiting for {because}.",
                fault);
        }
    }

    /// <summary>
    /// Probes once more past the deadline, reporting whether the condition held and, where the probe
    /// faulted rather than answering, what it threw.
    /// </summary>
    /// <remarks>
    /// The original deadline is spent by the time this runs, so the probe cannot reuse it — and the
    /// caller's bare token would leave the last await of a bounded wait unbounded, which is the hang
    /// this class exists to prevent. Its budget never exceeds the caller's own, so a short wait
    /// cannot be doubled by its own grace. A budget of its own reads as "did not hold"; a fault is
    /// returned rather than thrown, so it cannot displace the message naming what was awaited; and a
    /// cancellation the caller asked for still propagates.
    /// </remarks>
    private static async Task<(bool Held, Exception? Fault)> ProbeOnceMoreAsync(
        Func<CancellationToken, Task<bool>> probe,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var grace = CancellationTokenSource.CreateLinkedTokenSource(ct);
        grace.CancelAfter(timeout < GraceProbeTimeout ? timeout : GraceProbeTimeout);

        try
        {
            return (await probe(grace.Token), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, null);
        }
        catch (Exception fault) when (fault is not OperationCanceledException)
        {
            return (false, fault);
        }
    }
}
