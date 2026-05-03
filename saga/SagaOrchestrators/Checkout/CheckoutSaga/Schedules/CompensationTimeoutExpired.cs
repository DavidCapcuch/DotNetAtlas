namespace SagaOrchestrators.Checkout.CheckoutSaga.Schedules;

/// <summary>
/// Fires when a compensating state (<c>CompensatingStockReservations</c> or
/// <c>CompensatingPayment</c>) exceeds the configured
/// <c>Saga:CheckoutTimeouts:CompensationSeconds</c> budget (default 300s / 5min) per
/// docs/bc-design/checkout-saga.md § 7. Triggers the § 3 transition row that moves the
/// saga to the abnormal-terminal <c>CompensationStuck</c> with
/// <c>ErrorCode = COMPENSATION_TIMEOUT</c>; ops alert via <c>saga.checkout.stuck</c>.
/// </summary>
public sealed record CompensationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
