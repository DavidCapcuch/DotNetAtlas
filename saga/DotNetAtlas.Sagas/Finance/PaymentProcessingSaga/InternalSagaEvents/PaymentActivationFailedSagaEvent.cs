namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when subscription activation fails after payment capture.
/// Triggers a refund.
/// </summary>
public sealed record PaymentActivationFailedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription activation failed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID to refund.
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
