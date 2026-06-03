namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that Ordering confirmed the order. Adapted from the external
/// <c>Ordering.Orders.OrderConfirmedEvent</c> by the consumer adapter. Consumed in
/// <c>AwaitingConfirmation</c> (transition to terminal <c>Confirmed</c> per
/// docs/bc-design/checkout-saga.md § 4 transition table).
/// </summary>
public sealed record OrderConfirmedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id that was confirmed — the saga correlation key (ADR-0029); equals
    /// <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// UTC timestamp when Ordering reported the order confirmed.
    /// </summary>
    public required DateTimeOffset ConfirmedAtUtc { get; init; }
}
