namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when payment authorization fails.
/// </summary>
public sealed record PaymentAuthorizationFailedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id — the saga correlation key (ADR-0029).
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// User whose payment authorization failed.
    /// </summary>
    public required Guid UserId { get; init; }

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
    /// UTC timestamp when authorization failed.
    /// </summary>
    public required DateTime FailedAtUtc { get; init; }
}
