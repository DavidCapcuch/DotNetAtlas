namespace EShop.BFF.Api.Responses;

/// <summary>
/// Public landing page (bff.md § 3.4) — featured products + the full category tree + a stock overlay.
/// <see cref="CategoryTree"/> is <c>null</c> (and <see cref="HasStaleData"/> <c>true</c>) when Catalog's
/// category-tree read was unavailable; <see cref="StockHighlights"/> is <c>null</c> and every product's
/// availability fields are <c>null</c> (and <see cref="HasStaleData"/> <c>true</c>) when the Inventory
/// bulk overlay was unavailable. Either degradation still 200s; the endpoint adds the matching
/// <c>X-BFF-PartialData</c> header.
/// </summary>
public sealed record HomePageResponse
{
    /// <summary>The first page (up to 20) of active products from Catalog search, each enriched with availability.</summary>
    public required IReadOnlyList<FeaturedProductDto> FeaturedProducts { get; init; }

    /// <summary>The full category tree; <c>null</c> when Catalog's category-tree read was unavailable.</summary>
    public HomeCategoryTreeDto? CategoryTree { get; init; }

    /// <summary>
    /// "Running low" featured products (<c>0 &lt; AvailableQty &lt;= 10</c>); <c>null</c> when the Inventory
    /// bulk overlay was unavailable (availability unknown, so no highlights can be derived).
    /// </summary>
    public IReadOnlyList<StockHighlightDto>? StockHighlights { get; init; }

    /// <summary><c>true</c> when an upstream (category tree or stock overlay) was unavailable at composition time.</summary>
    public required bool HasStaleData { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }
}

public sealed record FeaturedProductDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string BrandName { get; init; }

    public required string CategoryBreadcrumb { get; init; }

    public required MoneyDto Price { get; init; }

    public string? PrimaryImageUrl { get; init; }

    /// <summary><c>Available &gt; 0</c>; <c>null</c> when stock availability is unknown.</summary>
    public bool? InStock { get; init; }

    /// <summary>Available quantity; <c>null</c> when stock availability is unknown.</summary>
    public int? AvailableQty { get; init; }
}

public sealed record HomeCategoryTreeDto
{
    public required IReadOnlyList<HomeCategoryNodeDto> Nodes { get; init; }
}

public sealed record HomeCategoryNodeDto
{
    public required Guid CategoryId { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    public Guid? ParentCategoryId { get; init; }

    public required int Depth { get; init; }

    public required int ProductCount { get; init; }
}

public sealed record StockHighlightDto(Guid ProductId, string Name, int AvailableQty);
