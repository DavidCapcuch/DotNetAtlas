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
    /// <exception cref="DataIntegrityException">
    /// <paramref name="sku"/> or <paramref name="name"/> is blank, or <paramref name="price"/> is
    /// not strictly positive — bug-class, so callers fail closed rather than branching on it.
    /// </exception>
    public static ProductSnapshot Create(string sku, string name, Money price, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(price);

        // Bug-class field invariants, enforced here because neither call site can be trusted to
        // have applied them — the ACL binds "" clean and MemoryPack rehydration enforces no
        // nullability. What a blank costs downstream, and why the two producers fail differently:
        // basket.md § 3.2. Price > 0 mirrors Catalog.Product; Money is sign-neutral (School B),
        // so that rule lives on the consuming VO.
        Throw.If(price.Amount <= 0, new DataIntegrityException(
            "Basket.ProductSnapshotPriceNotPositive",
            $"ProductSnapshot price must be strictly positive; was {price.Amount} {price.Currency.Name}."));

        // Each message carries the sibling field: on the batch ACL path a chunk covers up to 20
        // products and the throw escapes uncaught, so this is the only thing that says which one.
        Throw.If(string.IsNullOrWhiteSpace(sku), new DataIntegrityException(
            "Basket.ProductSnapshotSkuRequired",
            $"ProductSnapshot sku must be non-blank; name was '{name}'."));

        Throw.If(string.IsNullOrWhiteSpace(name), new DataIntegrityException(
            "Basket.ProductSnapshotNameRequired",
            $"ProductSnapshot name must be non-blank; sku was '{sku}'."));

        return new()
        {
            Sku = sku,
            Name = name,
            Price = price,
            CapturedAtUtc = capturedAtUtc,
        };
    }
}
