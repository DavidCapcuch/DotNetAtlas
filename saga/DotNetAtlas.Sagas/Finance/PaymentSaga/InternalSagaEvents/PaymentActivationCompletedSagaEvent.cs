namespace DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when the subscription has been successfully activated after payment capture.
/// </summary>
public sealed record PaymentActivationCompletedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription was activated.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID for the successful payment.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when activation was completed.
    /// </summary>
    public DateTime ActivatedAtUtc { get; init; }
}
