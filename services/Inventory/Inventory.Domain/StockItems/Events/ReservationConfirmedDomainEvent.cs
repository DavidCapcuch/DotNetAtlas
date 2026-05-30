using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Finalizes a reservation — stock physically leaves the warehouse.
/// </summary>
/// <remarks>
/// Reducer (let Q = reservation's quantity):
/// <c>OnHand -= Q, Reserved -= Q</c>; reservation transitions Active → Confirmed.
/// <c>Available</c> is mathematically unchanged because both operands drop equally.
/// </remarks>
public sealed record ReservationConfirmedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }

    public required Guid ReservationId { get; init; }

    public required DateTimeOffset ConfirmedAtUtc { get; init; }
}
