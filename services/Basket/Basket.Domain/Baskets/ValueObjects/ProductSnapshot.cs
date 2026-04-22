using Platform.SharedKernel.Base;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// Frozen copy of Catalog product data captured at the instant an item was added
/// to the basket (or replaced via RefreshBasketPrices). The linchpin of the ACL:
/// <see cref="IProductCatalogQueryPort"/> (Application layer) produces this VO,
/// and the Basket domain knows nothing about Catalog's wire DTOs.
/// </summary>
/// <remarks>
/// Frozen-pricing contract — once a snapshot is inside a basket it is immutable
/// until the user explicitly issues <c>RefreshBasketPricesCommand</c>. Checkout
/// commits to whatever snapshot is currently in the basket; it does not re-query
/// Catalog. See basket.md § 3.2.
/// </remarks>
/// <param name="Sku">Catalog SKU at the moment of capture.</param>
/// <param name="Name">Catalog product name at the moment of capture.</param>
/// <param name="Price">Catalog unit price at the moment of capture.</param>
/// <param name="CapturedAtUtc">UTC timestamp when the snapshot was taken.</param>
public sealed record ProductSnapshot(
    string Sku,
    string Name,
    Money Price,
    DateTimeOffset CapturedAtUtc) : ValueObject;
