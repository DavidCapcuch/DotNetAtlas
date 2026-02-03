namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when compensation (refund) for a failed subscription has been completed.
/// </summary>
public sealed record AlertSubscriptionPurchaseCompensationCompletedSagaEvent
{
    /// <summary>
    /// Correlation ID to link with the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user who received the refund.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID that was refunded.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// The refund transaction ID, if applicable.
    /// </summary>
    public Guid? RefundTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when compensation was completed.
    /// </summary>
    public required DateTime CompensatedAtUtc { get; init; }
}
