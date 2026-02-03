namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event indicating compensation (refund) has been completed.
/// </summary>
public sealed record AlertSubscriptionExtensionCompensationCompletedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was refunded.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Refund transaction ID.
    /// </summary>
    public required Guid RefundTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when compensation was completed.
    /// </summary>
    public required DateTime CompensatedAtUtc { get; init; }
}
