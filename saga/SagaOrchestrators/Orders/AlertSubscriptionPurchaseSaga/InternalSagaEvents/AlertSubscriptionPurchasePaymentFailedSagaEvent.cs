namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event indicating payment has failed terminally.
/// Mapped from Finance.Payments.PaymentFailedEvent by a Kafka consumer.
/// </summary>
public sealed record AlertSubscriptionPurchasePaymentFailedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment failed.
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
    /// UTC timestamp when payment failed.
    /// </summary>
    public required DateTime FailedAtUtc { get; init; }
}
