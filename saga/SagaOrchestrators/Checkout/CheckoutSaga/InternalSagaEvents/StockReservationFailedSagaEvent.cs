namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that Inventory could not satisfy a stock reservation. Adapted
/// from the external <c>Inventory.Reservations.StockReservationFailedEvent</c> by the M3
/// consumer adapter. Consumed in <c>AwaitingStockReservation</c> (transition to
/// <c>CompensatingStockReservations</c> per docs/bc-design/checkout-saga.md § 4 transition
/// table); first arrival wins, releases any reservations already accumulated.
/// </summary>
public sealed record StockReservationFailedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Product whose stock reservation failed.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Reservation id (saga-minted, echoed back by Inventory) of the failed entry. Required to
    /// mark the matching tracking entry as <c>Failed</c> in <c>ReservationIdsJson</c> per
    /// docs/bc-design/checkout-saga.md § 5.2 + § 8.1 Option B (Inventory echoes <c>ReservationId</c>
    /// it received on <c>ReserveStockCommand</c> back on every result event).
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Ordering aggregate id this reservation was attached to.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Quantity originally requested.
    /// </summary>
    public required int RequestedQuantity { get; init; }

    /// <summary>
    /// Quantity actually available at the time of the request - shortfall = requested - available.
    /// </summary>
    public required int AvailableQuantity { get; init; }

    /// <summary>
    /// Categorised failure code (e.g. <c>STOCK_UNAVAILABLE</c>).
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable failure message including shortfall details - aids ops forensics.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// UTC timestamp when Inventory reported the failure - mirrors the at-Utc field carried by
    /// every other failure / completion saga event.
    /// </summary>
    public required DateTimeOffset FailedAtUtc { get; init; }
}
