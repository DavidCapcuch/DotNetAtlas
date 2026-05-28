namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that Ordering created the order aggregate. Adapted from the
/// external <c>Ordering.Orders.OrderCreatedEvent</c> by the consumer adapter. Consumed in
/// state <c>AwaitingOrderCreation</c> (transition to <c>AwaitingStockReservation</c> per
/// docs/bc-design/checkout-saga.md § 4 transition table).
/// </summary>
public sealed record OrderCreatedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Ordering aggregate id captured into <c>CheckoutSagaState.OrderId</c>.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// UTC timestamp when Ordering reported the order created.
    /// </summary>
    public required DateTimeOffset OrderCreatedAtUtc { get; init; }
}
