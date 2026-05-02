namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event acknowledging that Inventory released a previously reserved stock entry.
/// Adapted from the external <c>Inventory.Reservations.ReservationReleasedEvent</c> by the M3
/// consumer adapter. Consumed in <c>CompensatingStockReservations</c> (one per in-flight
/// reservation) as one of the gating events for transition to terminal <c>Compensated</c> per
/// docs/bc-design/checkout-saga.md § 4 transition table. M4 will discriminate on
/// <see cref="ReleaseReason"/> to distinguish compensation-driven releases from TTL expiry.
/// </summary>
public sealed record ReservationReleasedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Product whose stock reservation was released.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Reservation id (saga-minted, echoed by Inventory) of the released entry.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Reason the reservation was released - typically "Compensation" or "Expiry".
    /// </summary>
    public required string ReleaseReason { get; init; }

    /// <summary>
    /// UTC timestamp when Inventory completed the release.
    /// </summary>
    public required DateTimeOffset ReleasedAtUtc { get; init; }
}
