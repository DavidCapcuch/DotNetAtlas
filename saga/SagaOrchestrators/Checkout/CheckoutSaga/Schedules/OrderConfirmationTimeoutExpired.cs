namespace SagaOrchestrators.Checkout.CheckoutSaga.Schedules;

/// <summary>
/// Fires when <c>AwaitingConfirmation</c> exceeds the configured
/// <c>Saga:CheckoutTimeouts:OrderConfirmationSeconds</c> budget (default 30s) per
/// docs/bc-design/checkout-saga.md § 7. Treated as an order-confirmation failure: triggers
/// the § 3 transition row that moves the saga to <c>CompensatingPayment</c> (refund-first)
/// with <c>ErrorCode = CONFIRMATION_TIMEOUT</c>.
/// </summary>
public sealed record OrderConfirmationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
