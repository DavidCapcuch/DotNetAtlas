namespace SagaOrchestrators.Finance.PaymentProcessingSaga.Schedules;

/// <summary>
/// Message sent when the success finalization timeout has expired for a saga instance.
/// This timeout fires after payment completion to finalize the saga if no refund was requested.
/// After this timeout, late refunds must be handled through a separate refund service.
/// </summary>
public sealed record SuccessFinalizationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
