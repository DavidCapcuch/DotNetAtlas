using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Drops a reservation without shipping — compensation, expiry, or cancellation.
/// </summary>
/// <remarks>
/// Reducer (let Q = reservation's quantity): <c>Reserved -= Q</c>;
/// reservation transitions Active → Released. <c>OnHand</c> is unchanged — stock
/// returns to availability for other reservations.
/// </remarks>
public sealed record ReservationReleasedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }

    public required Guid ReservationId { get; init; }

    public required ReleaseReason ReleaseReason { get; init; }

    public required DateTimeOffset ReleasedAtUtc { get; init; }
}
