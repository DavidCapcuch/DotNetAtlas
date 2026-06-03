namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that the Checkout saga has confirmed stock + order and is
/// approving capture of the authorized payment (ADR-0026 capture-at-the-pivot). Translated from
/// the external <c>Payments.Transactions.ApproveCaptureCommand</c> by
/// <c>ApproveCaptureCommandConsumer</c>. Drives the <c>AwaitingCaptureApproval → AwaitingCapture</c>
/// transition where the sub-saga issues <c>CapturePaymentCommand</c> to the Payments service.
/// </summary>
public sealed record ApproveCaptureSagaEvent
{
    /// <summary>
    /// Ordering aggregate id — the saga correlation key (ADR-0029).
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// User whose payment capture is being approved.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// UTC timestamp when the Checkout saga approved capture.
    /// </summary>
    public required DateTime RequestedAtUtc { get; init; }
}
