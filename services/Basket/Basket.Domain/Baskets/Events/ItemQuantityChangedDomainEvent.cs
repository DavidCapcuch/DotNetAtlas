using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.ChangeQuantity</c> when a line's
/// quantity is updated to a different value. Not raised when the new quantity
/// equals the existing quantity (idempotent no-op).
/// </summary>
public sealed record ItemQuantityChangedDomainEvent : DomainEvent
{
    public required Guid UserId { get; init; }

    public required Guid ProductId { get; init; }

    public required int OldQuantity { get; init; }

    public required int NewQuantity { get; init; }
}
