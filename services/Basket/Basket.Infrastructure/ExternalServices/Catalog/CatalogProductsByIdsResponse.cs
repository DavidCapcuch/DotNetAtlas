namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Basket's projection of Catalog's batch route
/// (<c>GET /api/v1/catalog/products/by-ids</c>), which backs the price refresh. Partial-tolerant by
/// contract: an id matching no product is simply absent from <see cref="Products"/>, and
/// <c>RefreshPrices</c> leaves that line untouched.
/// </summary>
/// <remarks>
/// Catalog also returns the ids that matched no product; Basket does not bind them, because absence
/// from <see cref="Products"/> already carries that signal.
/// </remarks>
internal sealed record CatalogProductsByIdsResponse
{
    public required IReadOnlyList<CatalogProductsByIdsItem> Products { get; init; }
}

/// <summary>
/// One item of the batch read, carrying only what the refresh reads per line: the id it merges on,
/// and the snapshot fields <c>RefreshPrices</c> writes back wholesale when a price moved.
/// </summary>
/// <remarks>
/// Owned by this route rather than shared with <see cref="CatalogProductByIdResponse"/>; the
/// ownership and strict-binding rules are stated once in basket.md &#xa7; 9.3. A null entry in
/// <see cref="CatalogProductsByIdsResponse.Products"/> is <em>not</em> caught by that strictness —
/// the adapter guards it explicitly.
/// </remarks>
internal sealed record CatalogProductsByIdsItem
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required CatalogPriceDto Price { get; init; }
}
