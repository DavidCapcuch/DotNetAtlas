namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that the Payments BC (via PaymentProcessingSaga) could not
/// capture payment. Adapted from the external <c>Payments.Transactions.PaymentFailedEvent</c>
/// by the consumer adapter (named <c>PaymentFailedCheckoutConsumer</c>). Consumed in
/// <c>AwaitingPayment</c> (transition to <c>CompensatingStockReservations</c> per
/// docs/bc-design/checkout-saga.md § 4 transition table). No refund is needed because payment
/// never captured.
/// </summary>
public sealed record PaymentFailedSagaEvent
{
    /// <summary>
    /// Order this payment is for — the saga correlation key (ADR-0029); equals
    /// <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Categorised failure code (e.g. <c>PAYMENT_FAILED</c>).
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable failure message - aids ops forensics.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// UTC timestamp when Payments reported the failure.
    /// </summary>
    public required DateTimeOffset FailedAtUtc { get; init; }
}
