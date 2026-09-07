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
    /// Polls <paramref name="probe"/> until it returns <c>true</c>.
    /// </summary>
    /// <param name="probe">
    /// The condition to poll. It is handed the deadline as a token and must pass it to whatever it
    /// awaits — a probe that blocks on a query with no timeout of its own is the one way this can
    /// still hang.
    /// </param>
    /// <param name="timeout">How long to keep polling before giving up.</param>
    /// <param name="because">
    /// What is being awaited, phrased to complete "waiting for ..."; becomes the timeout message.
    /// </param>
    /// <param name="ct">Cancellation token. Cancelling it propagates rather than becoming a timeout.</param>
    /// <exception cref="TimeoutException">Thrown when the condition never held.</exception>
    public static async Task UntilAsync(
        Func<CancellationToken, Task<bool>> probe,
        TimeSpan timeout,
        string because,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(because);

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
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0.#}s waiting for {because}.");
        }
    }
}
