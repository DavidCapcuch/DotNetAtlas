using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Records a successful hold of N units against an order. Internal ES event;
/// the external Avro counterpart in <c>Inventory.Reservations.StockReservedEvent</c>
/// is a separate type emitted by the outbox publisher via
/// <see cref="Inventory.Application.StockItems.ReserveStock.StockReservedMapper"/>.
/// </summary>
/// <remarks>
/// Reducer: <c>Reserved += Quantity</c>; reservation added with <c>Status = Active</c>.
/// </remarks>
public sealed record StockReservedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }

    public required Guid ReservationId { get; init; }

    public required int Quantity { get; init; }

    public required Guid OrderId { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
