using Platform.SharedKernel.Base;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// One line in the basket. Value object — no identity beyond structural equality;
/// references Catalog only by <see cref="ProductId"/> (Vernon rule 3) and holds
/// a frozen <see cref="ProductSnapshot"/> captured at add-time.
/// </summary>
/// <remarks>
/// <c>BasketItem</c> has no public validating factory — the <see cref="Basket"/>
/// aggregate is the single source of truth for the "quantity &gt;= 1" invariant
/// (see <see cref="Basket.AddItem"/> / <see cref="Basket.ChangeQuantity"/>).
/// Out-of-aggregate construction is intentionally not supported.
/// </remarks>
public sealed record BasketItem : ValueObject
{
    /// <summary>Catalog product identifier.</summary>
    public Guid ProductId { get; private init; }

    /// <summary>Frozen product data at the time the item was added or last refreshed.</summary>
    public ProductSnapshot Snapshot { get; private init; } = null!;

    /// <summary>Number of units. Always &gt;= 1.</summary>
    public int Quantity { get; private init; }

    private BasketItem()
    {
    }

    /// <summary>
    /// Trusted internal constructor — bypasses validation. Used by the aggregate
    /// for state transitions where invariants have already been enforced
    /// (first-add, quantity bumps, snapshot replacements during RefreshPrices),
    /// and by <c>BasketStateMapper</c> when rehydrating persisted state.
    /// </summary>
    internal static BasketItem BuildUnchecked(Guid productId, ProductSnapshot snapshot, int quantity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new()
        {
            ProductId = productId,
            Snapshot = snapshot,
            Quantity = quantity,
        };
    }
}
