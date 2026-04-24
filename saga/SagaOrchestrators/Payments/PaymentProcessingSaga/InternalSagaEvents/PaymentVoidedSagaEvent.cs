namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when an authorized payment has been voided (cancelled before capture).
/// </summary>
public sealed record PaymentVoidedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was voided.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID that was voided.
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// UTC timestamp when the void was completed.
    /// </summary>
    public required DateTime VoidedAtUtc { get; init; }
}
