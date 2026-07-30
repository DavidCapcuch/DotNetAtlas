using FluentResults;

namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// Typed client for Catalog's read surface (bff.md § 4.1). Returns <see cref="Result{T}"/> so callers
/// distinguish "product does not exist" (a gating <c>NotFoundError</c>) from "Catalog is unavailable"
/// (a <c>ServiceUnavailableError</c>).
/// </summary>
internal interface ICatalogClient
{
    Task<Result<CatalogProductDetailDto>> GetProductByIdAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Bulk product read (bff.md § 4.1) — backs the basket page's current-price / drift enrichment.
    /// Partial-tolerant: an id with no product is simply absent from
    /// <see cref="CatalogProductsByIdsDto.Products"/> (→ current price unknown). A failed result
    /// (transport / 5xx / an unbindable payload) is non-gating — the basket still renders with null current
    /// prices + a stale flag.
    /// </summary>
    Task<Result<CatalogProductsByIdsDto>> GetProductsByIdsAsync(IReadOnlyList<Guid> productIds, CancellationToken ct);

    /// <summary>
    /// Searches the product catalog (bff.md § 4.1). Backs the home page's "featured" set (first page of
    /// active products). A failed result is a transport/5xx failure (<c>ServiceUnavailableError</c>) — the
    /// home page treats it as the gating call (fail-safe stale or 503).
    /// </summary>
    Task<Result<PagedResult<CatalogProductSummaryDto>>> SearchProductsAsync(
        SearchProductsRequest request, CancellationToken ct);

    /// <summary>
    /// Reads the category tree (bff.md § 4.1). A failed result (transport / 5xx) is a non-gating partial for
    /// the home page — the tree is dropped (<c>categoryTree: null</c>) while featured products are kept.
    /// </summary>
    Task<Result<CategoryTreeDto>> GetCategoryTreeAsync(Guid? rootCategoryId, CancellationToken ct);
}
