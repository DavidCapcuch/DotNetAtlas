namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Cross-BC invariants that the Checkout saga depends on but that are owned by the Inventory
/// bounded context. Mirrors values the saga cannot read at runtime (Inventory does not yet
/// expose its TTL configurably) and that drive a build-time invariant assertion.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InventoryReservationTtlSeconds"/> mirrors
/// <c>Inventory.Application.StockItems.ReserveStock.ReserveStockCommandHandler.DefaultReservationTtl</c>
/// (currently <c>TimeSpan.FromMinutes(15)</c>, hard-coded). When Inventory exposes a
/// configurable <c>Inventory:ReservationTtlSeconds</c> setting, an Inventory architecture
/// test must cross-check this constant against that setting; until then this constant is
/// the agreed cross-BC invariant.
/// </para>
/// <para>
/// The invariant enforced by <c>CheckoutTimeoutInvariantTests</c> is:
/// <code>
/// OrderCreationSeconds + StockReservationSeconds + PaymentSeconds + OrderConfirmationSeconds
///   + 2 × CompensationSeconds
///   &lt; InventoryReservationTtlSeconds
/// </code>
/// so a worst-case happy-path-then-compensation cycle never outlives a stock reservation.
/// See <c>docs/bc-design/checkout-saga.md § 7.2</c> and ADR-0004 § Implementation Notes.
/// </para>
/// </remarks>
public static class InventoryReservationInvariants
{
    /// <summary>
    /// Maximum lifetime of an Inventory stock reservation, in seconds. Mirrors
    /// <c>ReserveStockCommandHandler.DefaultReservationTtl</c> (15 min).
    /// </summary>
    public const int InventoryReservationTtlSeconds = 900;
}
