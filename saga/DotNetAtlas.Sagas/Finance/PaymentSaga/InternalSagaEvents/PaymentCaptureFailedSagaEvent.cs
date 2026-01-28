namespace DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when payment capture fails.
/// The authorization should be voided.
/// </summary>
public sealed record PaymentCaptureFailedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment capture failed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID that failed to capture.
    /// </summary>
    public string AuthorizationId { get; init; } = string.Empty;

    /// <summary>
    /// Error code from the payment provider.
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether this failure is retryable.
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// UTC timestamp when capture failed.
    /// </summary>
    public DateTime FailedAtUtc { get; init; }
}
