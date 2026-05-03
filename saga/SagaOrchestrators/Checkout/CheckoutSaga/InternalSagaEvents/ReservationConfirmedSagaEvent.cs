namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event acknowledging that Inventory confirmed a previously reserved stock
/// entry. Adapted from the external <c>Inventory.Reservations.ReservationConfirmedEvent</c> by
/// the M3 consumer adapter. Consumed in <c>AwaitingConfirmation</c> for tracking only -
/// purely informational, does NOT gate transition to terminal <c>Confirmed</c> (Ordering's
/// confirm is the gate per docs/bc-design/checkout-saga.md § 4 transition table). Correlated
/// by <see cref="OrderId"/> per M3 plan-file § C1 Path B (Inventory's Avro lacks
/// <c>CorrelationId</c>).
/// </summary>
public sealed record ReservationConfirmedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id - the saga correlation key for this event under Path B.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Product whose stock reservation was confirmed.
    /// </summary>
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Reservation id (saga-minted, echoed by Inventory) of the confirmed entry.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// UTC timestamp when Inventory completed the confirm.
    /// </summary>
    public required DateTimeOffset ConfirmedAtUtc { get; init; }
}
