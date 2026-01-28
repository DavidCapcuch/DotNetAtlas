namespace DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when an authorized payment has been voided (cancelled before capture).
/// </summary>
public sealed record PaymentVoidedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was voided.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID that was voided.
    /// </summary>
    public string AuthorizationId { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the void was completed.
    /// </summary>
    public DateTime VoidedAtUtc { get; init; }
}
