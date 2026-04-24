using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Records a successful hold of N units against an order. Internal ES event — shares
/// the name with the external Avro event by design (see
/// <c>docs/bc-design/inventory.md</c> § 5).
/// </summary>
/// <remarks>
/// Reducer: <c>Reserved += Quantity</c>; reservation added with <c>Status = Active</c>.
/// </remarks>
public sealed record StockReservedEvent : DomainEvent
{
    public required Guid ProductId { get; init; }

    public required Guid ReservationId { get; init; }

    public required int Quantity { get; init; }

    public required Guid OrderId { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
