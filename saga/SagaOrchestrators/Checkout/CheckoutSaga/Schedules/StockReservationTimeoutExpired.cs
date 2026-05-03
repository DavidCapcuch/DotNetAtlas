namespace SagaOrchestrators.Checkout.CheckoutSaga.Schedules;

/// <summary>
/// Fires when <c>AwaitingStockReservation</c> exceeds the configured
/// <c>Saga:CheckoutTimeouts:StockReservationSeconds</c> budget (default 60s) per
/// docs/bc-design/checkout-saga.md § 7. Triggers the § 3 transition row that releases any
/// already-reserved stock and moves the saga to <c>CompensatingStockReservations</c> with
/// <c>ErrorCode = STOCK_TIMEOUT</c>.
/// </summary>
public sealed record StockReservationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
