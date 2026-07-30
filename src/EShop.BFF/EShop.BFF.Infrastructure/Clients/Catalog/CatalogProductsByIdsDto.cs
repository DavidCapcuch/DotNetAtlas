namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// BFF-internal projection of Catalog's <c>GET /api/v1/catalog/products/by-ids</c> response
/// (anti-corruption, bff.md § 4.1), backing the basket page's current-price / price-drift enrichment
/// (bff.md § 3.2). Catalog also returns the requested ids that matched no product; the BFF does not bind
/// them — a product absent from <see cref="Products"/> already renders as "current price unknown", so
/// binding the list would add a member no page reads (bff.md § 4.1).
/// </summary>
internal sealed record CatalogProductsByIdsDto
{
    public required IReadOnlyList<CatalogProductPricingDto> Products { get; init; }
}

/// <summary>
/// One item of the batch read, carrying only what the basket page renders per line: the id it merges on,
/// the current price it drift-checks against the snapshot, and the images it picks a primary thumbnail
/// from. Owned by this route rather than shared with
/// <see cref="CatalogProductDetailDto"/> — binding is strict, so every member declared here is a member
/// Catalog cannot drop from <em>this</em> route without degrading the basket page (bff.md § 4.1).
/// </summary>
internal sealed record CatalogProductPricingDto
{
    public required Guid ProductId { get; init; }

    public required CatalogMoneyDto Price { get; init; }

    public required IReadOnlyList<CatalogThumbnailDto> Images { get; init; }
}

/// <summary>An image the basket page ranks by <see cref="DisplayOrder"/> to pick the line's primary URL.</summary>
internal sealed record CatalogThumbnailDto
{
    public required string Url { get; init; }

    public required int DisplayOrder { get; init; }
}
