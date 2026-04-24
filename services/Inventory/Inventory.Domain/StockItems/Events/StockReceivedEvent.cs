using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Records an inbound stock movement — supplier delivery, customer return.
/// </summary>
/// <remarks>
/// ES event (persistence model). Reducer: <c>OnHand += Quantity</c>.
/// </remarks>
public sealed record StockReceivedEvent : DomainEvent
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }

    public required string Source { get; init; }

    public Guid? ReceivedByUserId { get; init; }
}
