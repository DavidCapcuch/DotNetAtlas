namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event acknowledging that Ordering cancelled the order during compensation.
/// Adapted from the external <c>Ordering.Orders.OrderCancelledEvent</c> by the consumer
/// adapter. Consumed in <c>CompensatingStockReservations</c> as one of the gating events for
/// transition to terminal <c>Compensated</c> per docs/bc-design/checkout-saga.md § 4 transition
/// table.
/// </summary>
public sealed record OrderCancelledSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Ordering aggregate id that was cancelled.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// UTC timestamp when Ordering completed the cancellation.
    /// </summary>
    public required DateTimeOffset CancelledAtUtc { get; init; }
}
