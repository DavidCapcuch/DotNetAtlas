namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// BFF-internal projection of Catalog's <c>GET /api/v1/catalog/products/{id}</c> response
/// (anti-corruption: upstream Catalog types never cross the BFF boundary, bff.md § 1 + § 4.1), carrying
/// the fields the product page renders. Owned by that one route: the batch read binds its own record
/// (<see cref="CatalogProductPricingDto"/>), because the two Catalog endpoints are independent contracts
/// free to diverge and each page reads a different subset (bff.md § 4.1).
/// </summary>
internal sealed record CatalogProductDetailDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string BrandName { get; init; }

    public required string CategoryPath { get; init; }

    public required string CategoryBreadcrumb { get; init; }

    public required CatalogMoneyDto Price { get; init; }

    public required string Status { get; init; }

    /// <summary><c>null</c> upstream for a product with no dimensions (digital / service products).</summary>
    public CatalogDimensionsDto? Dimensions { get; init; }

    public required IReadOnlyList<CatalogImageDto> Images { get; init; }
}

internal sealed record CatalogDimensionsDto
{
    public required decimal Length { get; init; }

    public required decimal Width { get; init; }

    public required decimal Height { get; init; }

    public required string Unit { get; init; }
}

/// <summary>
/// A product-page image: the URL, its ordering, and the alt text that page renders. The basket page reads
/// no alt text, so the batch read binds <see cref="CatalogThumbnailDto"/> instead — under strict binding a
/// shared image record would make <c>altText</c> a member Catalog could not drop from either route.
/// </summary>
internal sealed record CatalogImageDto
{
    public required string Url { get; init; }

    public required string AltText { get; init; }

    public required int DisplayOrder { get; init; }
}
