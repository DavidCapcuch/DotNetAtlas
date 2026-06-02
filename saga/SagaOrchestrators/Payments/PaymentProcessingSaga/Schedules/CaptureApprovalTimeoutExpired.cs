namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

/// <summary>
/// Message sent when the capture-approval wait-state timeout has expired for a saga instance —
/// the Checkout saga never signalled approval or abort (ADR-0026 wait-state timeout). Drives the
/// void path so the dangling authorization is released.
/// </summary>
public sealed record CaptureApprovalTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
