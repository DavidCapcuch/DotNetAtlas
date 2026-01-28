namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when a subscription has been successfully activated.
/// </summary>
public sealed record SubscriptionActivatedEvent
{
    /// <summary>
    /// Correlation ID to link with the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user whose subscription was activated.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID used for correlation.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when activation occurred.
    /// </summary>
    public DateTime ActivatedAtUtc { get; init; }
}
