namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event indicating payment has been completed successfully.
/// Mapped from Finance.Payments.PaymentCompletedEvent by a Kafka consumer.
/// </summary>
public sealed record AlertSubscriptionPurchasePaymentCompletedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was completed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID. Used for refunds if activation fails.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Amount that was charged.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// UTC timestamp when payment was completed.
    /// </summary>
    public required DateTime CompletedAtUtc { get; init; }
}
