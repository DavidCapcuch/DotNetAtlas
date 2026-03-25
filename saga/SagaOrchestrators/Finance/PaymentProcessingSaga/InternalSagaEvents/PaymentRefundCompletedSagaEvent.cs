namespace SagaOrchestrators.Finance.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when a refund for a captured payment has been completed.
/// </summary>
public sealed record PaymentRefundCompletedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User who received the refund.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID that was refunded.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// The refund transaction ID.
    /// </summary>
    public required Guid RefundTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when refund was completed.
    /// </summary>
    public required DateTime RefundedAtUtc { get; init; }
}
