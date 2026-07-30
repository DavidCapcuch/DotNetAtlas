namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// BFF-internal re-declaration of Catalog's product-search request (anti-corruption, bff.md § 4.1).
/// The home page uses only <see cref="Status"/> + paging today (featured = first page of active
/// products); the richer search facets in bff.md § 4.1 are added when a consumer (basket / search
/// slice) needs them, per YAGNI.
/// </summary>
internal sealed record SearchProductsRequest(string? Status, int PageNumber = 1, int PageSize = 20);

/// <summary>
/// BFF-internal projection of a single Catalog search hit — the trimmed product summary the home page
/// renders. Mirrors Catalog's <c>GET /api/v1/catalog/products</c> result item.
/// </summary>
internal sealed record CatalogProductSummaryDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string CategoryBreadcrumb { get; init; }

    public required string BrandName { get; init; }

    public required CatalogMoneyDto Price { get; init; }

    public required string Status { get; init; }

    /// <summary><c>null</c> upstream for a product with no images.</summary>
    public string? PrimaryImageUrl { get; init; }
}

/// <summary>
/// BFF-internal projection of an upstream paged envelope (anti-corruption, bff.md § 4.1). Binds only
/// <see cref="Items"/>: the BFF requests a fixed page and renders no pager, so upstream's total and echoed
/// paging parameters are members no page reads (bff.md § 4.1). A consumer that renders a pager binds them
/// then.
/// </summary>
internal sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
}
