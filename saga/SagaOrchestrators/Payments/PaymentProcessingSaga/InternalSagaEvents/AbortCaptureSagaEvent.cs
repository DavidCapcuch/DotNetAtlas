namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that the Checkout saga's confirmation step (stock + order)
/// failed and the authorized payment must be aborted (ADR-0026). Translated from the external
/// <c>Payments.Transactions.AbortCaptureCommand</c> by <c>AbortCaptureCommandConsumer</c>. Drives
/// the <c>AwaitingCaptureApproval → VoidInProgress</c> transition where the sub-saga issues
/// <c>VoidPaymentCommand</c> to the Payments service — a free pre-capture void, never a refund.
/// </summary>
public sealed record AbortCaptureSagaEvent
{
    /// <summary>
    /// Ordering aggregate id — the saga correlation key (ADR-0029).
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// User whose payment authorization is being aborted.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Reason the Checkout saga aborted the capture; flows onto the gateway void's audit trail.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// UTC timestamp when the Checkout saga aborted the capture.
    /// </summary>
    public required DateTime RequestedAtUtc { get; init; }
}
