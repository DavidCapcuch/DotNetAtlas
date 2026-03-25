namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when a subscription activation has failed.
/// </summary>
public sealed record AlertSubscriptionActivationFailedSagaEvent
{
    /// <summary>
    /// Correlation ID to link with the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user whose subscription activation failed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID for potential refund.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Error code categorizing the failure.
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Detailed error message.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// UTC timestamp when the failure occurred.
    /// </summary>
    public required DateTime FailedAtUtc { get; init; }

    /// <summary>
    /// Indicates whether this failure should trigger compensation (refund).
    /// </summary>
    public required bool ShouldCompensate { get; init; }
}
