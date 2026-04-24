namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when payment has been successfully captured.
/// Funds have been transferred.
/// </summary>
public sealed record PaymentCapturedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was captured.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID for the captured funds.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Authorization ID that was captured.
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// Captured amount.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// UTC timestamp when capture was completed.
    /// </summary>
    public required DateTime CapturedAtUtc { get; init; }
}
