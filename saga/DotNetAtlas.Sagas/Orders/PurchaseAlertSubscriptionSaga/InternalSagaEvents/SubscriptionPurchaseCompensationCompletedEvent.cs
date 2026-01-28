namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when compensation (refund) for a failed subscription has been completed.
/// </summary>
public sealed record SubscriptionPurchaseCompensationCompletedEvent
{
    /// <summary>
    /// Correlation ID to link with the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user who received the refund.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID that was refunded.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// The refund transaction ID, if applicable.
    /// </summary>
    public Guid? RefundTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when compensation was completed.
    /// </summary>
    public DateTime CompensatedAtUtc { get; init; }
}
