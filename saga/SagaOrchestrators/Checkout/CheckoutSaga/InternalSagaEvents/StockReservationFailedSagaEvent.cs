namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that Inventory could not satisfy a stock reservation. Adapted
/// from the external <c>Inventory.Reservations.StockReservationFailedEvent</c> by the
/// consumer adapter. Consumed in <c>AwaitingStockReservation</c> (transition to
/// <c>CompensatingStockReservations</c> per docs/bc-design/checkout-saga.md § 4 transition
/// table); first arrival wins, releases any reservations already accumulated. Correlated by
/// <see cref="OrderId"/> (Inventory's Avro lacks <c>CorrelationId</c>). The underlying
/// schema does not carry <c>ReservationId</c>, <c>ErrorCode</c> or <c>ErrorMessage</c>, so
/// the saga derives the in-flight tracking entry by <c>ProductId</c> instead (each ProductId
/// has at most one in-flight reservation per saga).
/// </summary>
public sealed record StockReservationFailedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id - the saga correlation key for this event under Path B.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Product whose stock reservation failed.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Quantity originally requested.
    /// </summary>
    public required int RequestedQuantity { get; init; }

    /// <summary>
    /// Quantity actually available at the time of the request - shortfall = requested - available.
    /// </summary>
    public required int AvailableQuantity { get; init; }

    /// <summary>
    /// UTC timestamp when Inventory reported the failure - mirrors the at-Utc field carried by
    /// every other failure / completion saga event.
    /// </summary>
    public required DateTimeOffset FailedAtUtc { get; init; }
}
