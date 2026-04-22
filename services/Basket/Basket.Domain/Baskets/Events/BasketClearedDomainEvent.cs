using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.Clear</c> after all items are removed.
/// The basket is not deleted; TTL is still reset on save. Only checkout deletes
/// the Redis entry.
/// </summary>
public sealed record BasketClearedDomainEvent : DomainEvent
{
    public required Guid UserId { get; init; }
}
