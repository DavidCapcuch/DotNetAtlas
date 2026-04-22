using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.RefreshPrices</c>. Lists only items
/// whose price actually changed — items whose price was re-fetched but matched
/// the existing snapshot are excluded. If no prices changed the event is not
/// raised at all.
/// </summary>
public sealed record BasketPricesRefreshedDomainEvent : DomainEvent
{
    public required Guid UserId { get; init; }

    public required IReadOnlyList<PriceChange> Changes { get; init; }
}

/// <summary>
/// One line's price change captured by <see cref="BasketPricesRefreshedDomainEvent"/>.
/// </summary>
public sealed record PriceChange(Guid ProductId, Money OldPrice, Money NewPrice);
