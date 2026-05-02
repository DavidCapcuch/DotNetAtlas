namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event acknowledging that the Payments BC (via PaymentProcessingSaga) refunded
/// a previously captured payment. Adapted from the external
/// <c>Payments.Transactions.PaymentRefundedEvent</c> by the M3 consumer adapter (named
/// <c>PaymentRefundedCheckoutConsumer</c>). Consumed in <c>CompensatingPayment</c> (transition
/// to <c>CompensatingStockReservations</c> for the second compensation phase per
/// docs/bc-design/checkout-saga.md § 4 transition table + § 6.1 two-phase rationale).
/// </summary>
public sealed record PaymentRefundedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Payment transaction id that was refunded.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Refunded amount.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// UTC timestamp when Payments completed the refund.
    /// </summary>
    public required DateTimeOffset RefundedAtUtc { get; init; }
}
