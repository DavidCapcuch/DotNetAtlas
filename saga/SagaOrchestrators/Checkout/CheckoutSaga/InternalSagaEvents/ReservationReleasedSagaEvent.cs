namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event acknowledging that Inventory released a previously reserved stock entry.
/// Adapted from the external <c>Inventory.Reservations.ReservationReleasedEvent</c> by the
/// consumer adapter. Consumed in <c>CompensatingStockReservations</c> (one per in-flight
/// reservation) as one of the gating events for transition to terminal <c>Compensated</c> per
/// docs/bc-design/checkout-saga.md § 4 transition table; the saga discriminates on
/// <see cref="ReleaseReason"/> to distinguish compensation-driven releases from TTL expiry.
/// Correlated by <see cref="OrderId"/> (Inventory's Avro lacks <c>CorrelationId</c>).
/// </summary>
public sealed record ReservationReleasedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id - the saga correlation key for this event under Path B.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Product whose stock reservation was released.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Reservation id (saga-minted, echoed by Inventory) of the released entry.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Reason the reservation was released - "Compensation", "Expiry", or "Cancellation".
    /// Sourced from <c>Inventory.Reservations.ReleaseReason</c> Avro enum (consumer adapter
    /// converts to string via <c>.ToString()</c>).
    /// </summary>
    public required string ReleaseReason { get; init; }

    /// <summary>
    /// UTC timestamp when Inventory completed the release.
    /// </summary>
    public required DateTimeOffset ReleasedAtUtc { get; init; }
}
