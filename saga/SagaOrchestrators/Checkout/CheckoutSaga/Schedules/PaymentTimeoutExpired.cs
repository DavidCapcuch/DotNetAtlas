namespace SagaOrchestrators.Checkout.CheckoutSaga.Schedules;

/// <summary>
/// Fires when <c>AwaitingPayment</c> exceeds the configured
/// <c>Saga:CheckoutTimeouts:PaymentSeconds</c> budget (default 90s) per
/// docs/bc-design/checkout-saga.md § 7. Treated as a payment failure: triggers the § 3
/// transition row that moves the saga to <c>CompensatingStockReservations</c> with
/// <c>ErrorCode = PAYMENT_TIMEOUT</c>. Note the captured-but-compensated mitigation in § 7
/// — the outer timeout must be ≥ <c>PaymentProcessingSaga</c>'s authorize+capture sum.
/// </summary>
public sealed record PaymentTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
