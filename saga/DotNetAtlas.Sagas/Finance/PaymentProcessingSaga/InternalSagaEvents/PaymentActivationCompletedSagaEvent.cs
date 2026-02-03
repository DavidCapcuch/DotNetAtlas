namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when the subscription has been successfully activated after payment capture.
/// </summary>
public sealed record PaymentActivationCompletedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription was activated.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID for the successful payment.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when activation was completed.
    /// </summary>
    public required DateTime ActivatedAtUtc { get; init; }
}
