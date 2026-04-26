using Basket.Domain.Baskets.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// One line in the basket. Value object — no identity beyond structural equality;
/// references Catalog only by <see cref="ProductId"/> (Vernon rule 3) and holds
/// a frozen <see cref="ProductSnapshot"/> captured at add-time.
/// </summary>
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
    /// Creates a validated <see cref="BasketItem"/>.
    /// </summary>
    /// <returns>
    /// Success with the item, or <see cref="BasketItemErrors.InvalidQuantity"/> when
    /// <paramref name="quantity"/> is less than 1.
    /// </returns>
    public static Result<BasketItem> Create(Guid productId, ProductSnapshot snapshot, int quantity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (quantity < 1)
        {
            return Result.Fail<BasketItem>(BasketItemErrors.InvalidQuantity());
        }

        return Result.Ok(BuildUnchecked(productId, snapshot, quantity));
    }

    /// <summary>
    /// Trusted internal constructor — bypasses validation. Used only by the aggregate
    /// for state transitions where invariants have already been enforced (quantity bumps,
    /// snapshot replacements during refresh-prices).
    /// </summary>
    internal static BasketItem BuildUnchecked(Guid productId, ProductSnapshot snapshot, int quantity) =>
        new()
        {
            ProductId = productId,
            Snapshot = snapshot,
            Quantity = quantity,
        };
}
