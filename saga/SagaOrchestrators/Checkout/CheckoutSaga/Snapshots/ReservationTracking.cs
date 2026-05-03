namespace SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;

/// <summary>
/// Tracks one product's reservation lifecycle within a Checkout saga - the value type of the
/// <c>IDictionary&lt;Guid ProductId, ReservationTracking&gt;</c> serialized into
/// <c>CheckoutSagaState.ReservationIdsJson</c>. Updated on every Stock* / Reservation* saga
/// event per docs/bc-design/checkout-saga.md § 2 + § 5.2.
/// </summary>
/// <param name="Status">One of <see cref="ReservationStatus"/>.</param>
/// <param name="ReservationId">Saga-minted reservation id (UUID v7) - present once Status == Reserved.</param>
/// <param name="ReservedAtUtc">UTC timestamp echoed by Inventory when the reservation was created.</param>
/// <param name="ExpiresAtUtc">UTC timestamp at which Inventory's TTL worker will auto-release the reservation.</param>
internal sealed record ReservationTracking(
    string Status,
    Guid? ReservationId,
    DateTimeOffset? ReservedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// String-literal status constants used in <see cref="ReservationTracking.Status"/>. Mirrors
/// the spec-defined lifecycle from docs/bc-design/checkout-saga.md § 2 / § 5.2.
/// </summary>
internal static class ReservationStatus
{
    public const string Pending = "Pending";
    public const string Reserved = "Reserved";
    public const string Failed = "Failed";
    public const string Released = "Released";
    public const string Confirmed = "Confirmed";
}
