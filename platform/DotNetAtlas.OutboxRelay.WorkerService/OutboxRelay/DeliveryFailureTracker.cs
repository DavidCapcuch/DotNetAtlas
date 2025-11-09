namespace DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;

/// <summary>
/// Simplified tracker that records only the earliest failed message ID.
/// Uses Interlocked operations for thread-safe tracking from delivery report background thread.
/// </summary>
internal sealed class DeliveryFailureTracker : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ILogger _logger;
    private long _earliestFailedMessageId = long.MinValue;

    public DeliveryFailureTracker(
        ILogger logger,
        CancellationToken parentCancellationToken)
    {
        _logger = logger;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>
    /// Records a delivery failure. Captures only the earliest (lowest ID) failed message.
    /// Thread-safe using Interlocked.CompareExchange in a loop.
    /// </summary>
    public void RecordFailure(long messageId)
    {
        long currentEarliestId;
        long newValue;

        // Thread safe exchange without locking
        // See https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.compareexchange?view=net-9.0#system-threading-interlocked-compareexchange(system-single@-system-single-system-single)
        do
        {
            currentEarliestId = Interlocked.Read(ref _earliestFailedMessageId);

            if (messageId >= currentEarliestId && currentEarliestId != long.MinValue)
            {
                return; // not the earliest failure id, ignore
            }

            newValue = messageId;
        }
        while (Interlocked.CompareExchange(ref _earliestFailedMessageId, newValue, currentEarliestId)
               != currentEarliestId);

        _logger.LogWarning(
            "Delivery failure detected for message id {MessageId}. Requesting cancellation", messageId);

        _cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Gets the earliest failed message ID, or null if no failures occurred.
    /// </summary>
    public long? GetEarliestFailedMessageId()
    {
        var earliestFailedMessageId = Interlocked.Read(ref _earliestFailedMessageId);

        return earliestFailedMessageId == long.MinValue ? null : earliestFailedMessageId;
    }

    public void Dispose() => _cancellationTokenSource.Dispose();
}
