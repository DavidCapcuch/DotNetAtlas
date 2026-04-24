namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Private projection of Catalog's <c>GetProductByIdResponse</c> HTTP contract —
/// contains only the fields Basket needs (<c>ProductId</c>, <c>Sku</c>,
/// <c>Name</c>, <c>Price</c>). Additional fields on Catalog's wire response
/// (<c>Description</c>, <c>CategoryId</c>, <c>CategoryPath</c>,
/// <c>CategoryBreadcrumb</c>, <c>BrandName</c>, <c>Status</c>,
/// <c>Dimensions</c>, <c>Images</c>, <c>CreatedAtUtc</c>,
/// <c>LastUpdatedAtUtc</c>) are silently dropped by
/// <c>System.Text.Json</c> during deserialization — exactly the intent of the
/// Anti-Corruption Layer (basket.md &#xa7; 9.3).
/// </summary>
internal sealed record CatalogProductResponse(
    Guid ProductId,
    string Sku,
    string Name,
    CatalogPriceDto Price);
