using Basket.Domain.Baskets.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// One line in the basket. Value object — no identity beyond structural equality;
/// references Catalog only by <see cref="ProductId"/> (Vernon rule 3) and holds
/// a frozen <see cref="ProductSnapshot"/> captured at add-time.
/// </summary>
/// <param name="ProductId">Catalog product identifier.</param>
/// <param name="Snapshot">Frozen product data at the time the item was added or last refreshed.</param>
/// <param name="Quantity">Number of units. Must be &gt;= 1 (validated in <see cref="Create"/>).</param>
public sealed record BasketItem(
    Guid ProductId,
    ProductSnapshot Snapshot,
    int Quantity) : ValueObject
{
    /// <summary>
    /// Creates a validated <see cref="BasketItem"/>.
    /// </summary>
    /// <param name="productId">Catalog product identifier.</param>
    /// <param name="snapshot">Frozen product data.</param>
    /// <param name="quantity">Number of units (must be &gt;= 1).</param>
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

        return Result.Ok(new BasketItem(productId, snapshot, quantity));
    }
}
