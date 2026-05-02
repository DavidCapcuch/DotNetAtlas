namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event acknowledging that Inventory confirmed a previously reserved stock
/// entry. Adapted from the external <c>Inventory.Reservations.ReservationConfirmedEvent</c> by
/// the M3 consumer adapter. Consumed in <c>AwaitingConfirmation</c> for tracking only -
/// purely informational, does NOT gate transition to terminal <c>Confirmed</c> (Ordering's
/// confirm is the gate per docs/bc-design/checkout-saga.md § 4 transition table).
/// </summary>
public sealed record ReservationConfirmedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

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
