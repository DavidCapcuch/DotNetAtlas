namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Basket's projection of Catalog's product-detail route
/// (<c>GET /api/v1/catalog/products/{id}</c>), which backs snapshot capture on add-item. Owned by
/// this route rather than shared with <see cref="CatalogProductsByIdsItem"/>; the ownership and
/// strict-binding rules are stated once in basket.md &#xa7; 9.3.
/// </summary>
/// <remarks>
/// The product id is deliberately unbound — the caller supplied it, so nothing reads it back.
/// </remarks>
internal sealed record CatalogProductByIdResponse
{
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required CatalogPriceDto Price { get; init; }
}
