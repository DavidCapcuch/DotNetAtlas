namespace SagaOrchestrators.Finance.PaymentProcessingSaga.Schedules;

/// <summary>
/// Message sent when the capture timeout has expired for a saga instance.
/// </summary>
public sealed record CaptureTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
