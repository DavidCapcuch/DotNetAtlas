namespace EShop.BFF.Api.Responses;

/// <summary>
/// Authenticated buyer's basket (bff.md § 3.2) — Basket's snapshot lines overlaid with <em>current</em>
/// Catalog price (price-drift flags) and <em>current</em> Inventory availability (out-of-stock flags), so
/// the UI can warn "price changed / out of stock since you added" without a refresh. A per-item
/// <c>CurrentPrice</c> / <c>AvailableQty</c> is <c>null</c> when that enrichment was unavailable (the Catalog
/// or Inventory batch failed, or omitted the product) — in which case <see cref="HasStaleData"/> is set and
/// the endpoint adds <c>X-BFF-PartialData</c>.
/// </summary>
public sealed record BasketPageResponse
{
    public required Guid UserId { get; init; }

    /// <summary>Basket optimistic-concurrency version (passed through from Basket).</summary>
    public required int Version { get; init; }

    public required IReadOnlyList<BasketPageItemDto> Items { get; init; }

    /// <summary>Sum of <c>SnapshotPrice × Quantity</c> — what the buyer saw when adding.</summary>
    public required MoneyDto TotalSnapshot { get; init; }

    /// <summary>Sum of <c>(CurrentPrice ?? SnapshotPrice) × Quantity</c> — defensive fallback to snapshot.</summary>
    public required MoneyDto TotalCurrent { get; init; }

    /// <summary><c>true</c> when any item's price drifted since it was added.</summary>
    public required bool HasPriceDrift { get; init; }

    /// <summary><c>true</c> when any item is now short of the basket quantity.</summary>
    public required bool HasOutOfStock { get; init; }

    /// <summary><c>true</c> when any enrichment was missing (Catalog/Inventory batch failed or partial).</summary>
    public required bool HasStaleData { get; init; }

    /// <summary>
    /// Compose timestamp. Load-bearing: the endpoint infers a fail-safe stale serve from this value's age
    /// (FusionCache exposes no "served stale" flag) — see <c>BffBasketCache.StaleServeFreshWindow</c>.
    /// </summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed record BasketPageItemDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    /// <summary>Unit price captured by Basket at add-time.</summary>
    public required MoneyDto SnapshotPrice { get; init; }

    /// <summary>Current unit price from Catalog; <c>null</c> when Catalog was unavailable or omitted the product.</summary>
    public MoneyDto? CurrentPrice { get; init; }

    /// <summary><c>true</c> when <see cref="CurrentPrice"/> is known and differs from <see cref="SnapshotPrice"/>.</summary>
    public required bool PriceDrifted { get; init; }

    /// <summary><c>SnapshotPrice × Quantity</c>.</summary>
    public required MoneyDto LineTotalSnapshot { get; init; }

    /// <summary><c>(CurrentPrice ?? SnapshotPrice) × Quantity</c>.</summary>
    public required MoneyDto LineTotalCurrent { get; init; }

    /// <summary>Current available quantity from Inventory; <c>null</c> when unavailable or omitted.</summary>
    public int? AvailableQty { get; init; }

    /// <summary><c>true</c> when <see cref="AvailableQty"/> is known and below the basket <see cref="Quantity"/>.</summary>
    public required bool OutOfStock { get; init; }

    /// <summary>Primary image URL from current Catalog data; <c>null</c> when unavailable.</summary>
    public string? PrimaryImageUrl { get; init; }
}
