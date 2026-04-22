using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.Create</c> when a brand-new basket
/// is lazily created on the user's first <c>AddItemToBasketCommand</c>. Never
/// published to Kafka (basket.md § 3 / § 7).
/// </summary>
public sealed record BasketCreatedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the user who owns the basket (also the aggregate id).
    /// </summary>
    public required Guid UserId { get; init; }
}
