using Platform.SharedKernel.ValueObjects;

namespace Ordering.Domain.Baskets;

/// <summary>
/// ACL input DTO consumed by <c>Order.CreateFromBasket</c>. Represents the
/// frozen state of a Basket at checkout time. Not persisted — the Order
/// aggregate translates this into its own <c>OrderItem</c> + <c>ProductSnapshot</c>
/// types (<c>ordering.md § 4.8</c>, § 10.1 Anti-Corruption Layer).
/// </summary>
public sealed record BasketSnapshot(
    Guid BuyerId,
    CurrencyCode Currency,
    IReadOnlyCollection<BasketSnapshotItem> Items);
