namespace Ordering.Domain.Baskets;

/// <summary>
/// A single line in a <see cref="BasketSnapshot"/>. Primitive shape on purpose
/// — keeps Basket free to ship any format; Ordering maps these into its own
/// strongly-typed <c>OrderItem</c> + <c>Money</c> + <c>ProductSnapshot</c>
/// value objects in the <c>CreateFromBasket</c> factory.
/// </summary>
/// <remarks>
/// Currency is intentionally omitted at this level — it is inherited from the
/// parent <see cref="BasketSnapshot.Currency"/> so invariant I-9 ("single
/// currency across all items", <c>ordering.md § 3.1</c>) is enforced by
/// construction. If this contract ever gains a per-item currency, the
/// <c>CreateFromBasket</c> factory must add an explicit I-9 check.
/// </remarks>
public sealed record BasketSnapshotItem(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount);
