using Inventory.Domain.StockItems.ValueObjects;

namespace Inventory.Application.Common.ReadModels;

/// <summary>
/// Read-model row for <c>inventory.reservation_audit</c> — the ops /
/// expiry-worker projection described in <c>inventory.md</c> § 9.2. One row
/// per reservation; lifecycle fields (<see cref="Status"/>,
/// <see cref="ResolvedAtUtc"/>, <see cref="ReleaseReason"/>) mutate as the
/// reservation transitions through Active → Confirmed / Released.
/// </summary>
public sealed class ReservationAuditRow
{
    /// <summary>Reservation id (= aggregate-local ReservationId).</summary>
    public Guid ReservationId { get; set; }

    /// <summary>Product stream id — joins back to <c>current_stock_levels</c>.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Owning order. Enables fan-in queries ("all reservations for order X").</summary>
    public Guid OrderId { get; set; }

    /// <summary>Units reserved. Immutable after the initial insert.</summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Lifecycle status. <see cref="ReservationStatus.Active"/> on insert; flipped
    /// to <see cref="ReservationStatus.Confirmed"/> or
    /// <see cref="ReservationStatus.Released"/> by the later events.
    /// </summary>
    public ReservationStatus Status { get; set; }

    /// <summary>UTC timestamp the reservation was created.</summary>
    public DateTimeOffset ReservedAtUtc { get; set; }

    /// <summary>
    /// UTC expiry (= <see cref="ReservedAtUtc"/> + TTL). Drives the M6
    /// <c>ReservationExpiryWorker</c> scan:
    /// <c>WHERE Status='Active' AND ExpiresAtUtc &lt; now()</c>.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the terminal transition (Confirmed / Released). Null
    /// while the reservation is Active.
    /// </summary>
    public DateTimeOffset? ResolvedAtUtc { get; set; }

    /// <summary>
    /// Populated only when <see cref="Status"/> is
    /// <see cref="ReservationStatus.Released"/>. Carried through to the
    /// external <c>ReservationReleasedEvent.ReleaseReason</c>.
    /// </summary>
    public ReleaseReason? ReleaseReason { get; set; }
}
