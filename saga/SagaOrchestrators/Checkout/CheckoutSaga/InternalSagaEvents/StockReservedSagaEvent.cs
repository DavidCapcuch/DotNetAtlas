namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that Inventory reserved stock for one ProductId. Adapted from
/// the external <c>Inventory.Reservations.StockReservedEvent</c> by the M3 consumer adapter.
/// Per docs/bc-design/checkout-saga.md § 8.1 Option B, the external event carries
/// <see cref="CorrelationId"/> directly (echoed back from <c>ReserveStockCommand</c>) so the
/// adapter does not need a side-table lookup. Consumed (one per distinct ProductId) in
/// <c>AwaitingStockReservation</c>; the saga stays in state until <c>PendingReservations</c>
/// reaches zero, then transitions to <c>AwaitingPayment</c> per § 4 transition table.
/// </summary>
public sealed record StockReservedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Product whose stock was reserved.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Reservation id minted client-side by the saga and echoed back by Inventory; the portable
    /// idempotency key for compensation per docs/bc-design/checkout-saga.md § 5.5.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Ordering aggregate id this reservation is attached to.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Quantity reserved for this ProductId (sum across the basket's lines for that ProductId).
    /// </summary>
    public required int Quantity { get; init; }

    /// <summary>
    /// UTC timestamp when Inventory completed the reservation.
    /// </summary>
    public required DateTimeOffset ReservedAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp at which the reservation expires unless confirmed (Inventory TTL,
    /// 900 seconds per inventory.md).
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
