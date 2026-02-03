namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when a subscription has been successfully activated.
/// </summary>
public sealed record AlertSubscriptionActivatedSagaEvent
{
    /// <summary>
    /// Correlation ID to link with the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user whose subscription was activated.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID used for correlation.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when activation occurred.
    /// </summary>
    public required DateTime ActivatedAtUtc { get; init; }
}
