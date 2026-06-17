namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// BFF-internal re-declaration of Catalog's product read model (anti-corruption:
/// upstream Catalog types never cross the BFF boundary, bff.md § 1 + § 4.1). A trimmed
/// projection of Catalog's <c>GET /api/v1/catalog/products/{id}</c> response carrying only
/// the fields the product page renders.
/// </summary>
internal sealed record CatalogProductDto(
    Guid ProductId,
    string Sku,
    string Name,
    string Description,
    string BrandName,
    string CategoryPath,
    string CategoryBreadcrumb,
    CatalogMoneyDto Price,
    string Status,
    CatalogDimensionsDto? Dimensions,
    IReadOnlyList<CatalogImageDto> Images);

internal sealed record CatalogMoneyDto(decimal Amount, string Currency);

internal sealed record CatalogDimensionsDto(decimal Length, decimal Width, decimal Height, string Unit);

internal sealed record CatalogImageDto(string Url, string AltText, int DisplayOrder);

/// <summary>
/// BFF-internal re-declaration of Catalog's bulk product read (anti-corruption, bff.md § 4.1) — Catalog's
/// <c>GET /api/v1/catalog/products/by-ids</c> response: the found products plus the requested ids that had
/// no product (<see cref="MissingProductIds"/> → "current price unknown"). Backs the basket page's
/// current-price / price-drift enrichment (bff.md § 3.2).
/// </summary>
internal sealed record CatalogProductsByIdsDto(
    IReadOnlyList<CatalogProductDto> Products,
    IReadOnlyList<Guid> MissingProductIds);
