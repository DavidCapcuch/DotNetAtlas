using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.RemoveItem</c> when a product that was
/// present in the basket is removed. Not raised on the idempotent no-op path
/// (removal of a product that is not in the basket returns
/// <c>Result.Ok</c> without an event).
/// </summary>
public sealed record ItemRemovedFromBasketDomainEvent : DomainEvent
{
    public required Guid UserId { get; init; }

    public required Guid ProductId { get; init; }
}
