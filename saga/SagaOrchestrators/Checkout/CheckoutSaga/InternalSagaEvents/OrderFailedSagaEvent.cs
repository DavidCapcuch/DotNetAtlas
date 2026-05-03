namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that Ordering rejected or failed an order command. Adapted
/// from the external <c>Ordering.Orders.OrderFailedEvent</c> by the M3 consumer adapter.
/// Consumed in either <c>AwaitingOrderCreation</c> (transition to terminal <c>Failed</c>) or
/// <c>AwaitingConfirmation</c> (transition to <c>CompensatingPayment</c> for refund-first
/// compensation) per docs/bc-design/checkout-saga.md § 4 transition table.
/// </summary>
public sealed record OrderFailedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Ordering aggregate id. Always populated by the Avro producer
    /// (<c>Ordering.Orders.OrderFailedEvent.OrderId</c> is a non-nullable uuid); the field is
    /// kept nullable here so M4 can null it deliberately during PII / state cleanup if needed.
    /// Producer convention for pre-creation validation rejects is to emit <c>Guid.Empty</c>,
    /// which the saga discriminates via the producing-state context, not via null.
    /// </summary>
    public Guid? OrderId { get; init; }

    /// <summary>
    /// Categorised failure code (e.g. <c>ORDER_VALIDATION_FAILED</c>, <c>CONFIRMATION_FAILED</c>).
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable failure message - aids ops forensics.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// UTC timestamp when Ordering reported the failure - mirrors the at-Utc field carried by
    /// every other failure / completion saga event.
    /// </summary>
    public required DateTimeOffset FailedAtUtc { get; init; }
}
