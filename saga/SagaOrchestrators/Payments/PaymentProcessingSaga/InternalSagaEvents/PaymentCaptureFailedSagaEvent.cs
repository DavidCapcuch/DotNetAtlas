namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when payment capture fails.
/// The authorization should be voided.
/// </summary>
public sealed record PaymentCaptureFailedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id — the saga correlation key (ADR-0029).
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// User whose payment capture failed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID that failed to capture.
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// Error code from the payment provider.
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Indicates whether this failure is retryable.
    /// </summary>
    public required bool IsRetryable { get; init; }

    /// <summary>
    /// UTC timestamp when capture failed.
    /// </summary>
    public required DateTime FailedAtUtc { get; init; }
}
