namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// Why a reservation was released. Carried on every <c>ReservationReleasedDomainEvent</c> and
/// <c>ReleaseReservationCommand</c>. Critical for ops/auditing — a release is never
/// "just a release.".
/// </summary>
public enum ReleaseReason
{
    /// <summary>Saga compensation — a downstream step failed, reversing this reservation.</summary>
    Compensation = 0,

    /// <summary>TTL elapsed without confirmation — the reservation expired.</summary>
    Expiry = 1,

    /// <summary>Explicit user or admin cancellation.</summary>
    Cancellation = 2,
}
