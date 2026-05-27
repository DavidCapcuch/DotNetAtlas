using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// Frozen copy of Catalog product data captured at the instant an item was added
/// to the basket (or replaced via RefreshBasketPrices). The linchpin of the ACL:
/// <see cref="Application.Abstractions.IProductCatalogQueryPort"/> (Application layer) produces this VO,
/// and the Basket domain knows nothing about Catalog's wire DTOs.
/// </summary>
/// <remarks>
/// Frozen-pricing contract — once a snapshot is inside a basket it is immutable
/// until the user explicitly issues <c>RefreshBasketPricesCommand</c>. Checkout
/// commits to whatever snapshot is currently in the basket; it does not re-query
/// Catalog. See basket.md § 3.2.
/// </remarks>
public sealed record ProductSnapshot : ValueObject
{
    /// <summary>Catalog SKU at the moment of capture.</summary>
    public string Sku { get; private init; } = null!;

    /// <summary>Catalog product name at the moment of capture.</summary>
    public string Name { get; private init; } = null!;

    /// <summary>Catalog unit price at the moment of capture.</summary>
    public Money Price { get; private init; } = null!;

    /// <summary>UTC timestamp when the snapshot was taken.</summary>
    public DateTimeOffset CapturedAtUtc { get; private init; }

    private ProductSnapshot()
    {
    }

    /// <summary>
    /// Creates a frozen snapshot of Catalog product data.
    /// </summary>
    public static ProductSnapshot Create(string sku, string name, Money price, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(price);

        // Basket-local invariant: Price > 0. Mirrors Catalog.Product; Money itself is sign-neutral
        // (School B), so the rule lives on the consuming VO. Bug-class: both call sites
        // (ProductCatalogHttpAdapter, BasketStateMapper) cross from already-validated upstreams.
        Throw.If(price.Amount <= 0, new DataIntegrityException(
            "Basket.ProductSnapshotPriceNotPositive",
            $"ProductSnapshot price must be strictly positive; was {price.Amount} {price.Currency.Name}."));

        return new()
        {
            Sku = sku,
            Name = name,
            Price = price,
            CapturedAtUtc = capturedAtUtc,
        };
    }
}
