namespace SagaOrchestrators.Finance.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when a refund is requested.
/// </summary>
public sealed record PaymentRefundRequestedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User to refund.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID to refund.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Reason for the refund request.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// UTC timestamp when the refund was requested.
    /// </summary>
    public required DateTime RequestedAtUtc { get; init; }
}
