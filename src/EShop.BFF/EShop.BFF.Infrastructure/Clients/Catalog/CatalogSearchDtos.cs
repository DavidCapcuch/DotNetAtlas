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
internal sealed record CatalogProductSummaryDto(
    Guid ProductId,
    string Sku,
    string Name,
    string CategoryBreadcrumb,
    string BrandName,
    CatalogMoneyDto Price,
    string Status,
    string? PrimaryImageUrl);

/// <summary>BFF-internal paged result envelope (anti-corruption, bff.md § 4.1).</summary>
internal sealed record PagedResult<T>(int Total, int PageNumber, int PageSize, IReadOnlyList<T> Items);
