namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// BFF-internal re-declaration of Catalog's category tree (anti-corruption, bff.md § 4.1). Mirrors
/// Catalog's <c>GET /api/v1/catalog/categories/tree</c> response — a flat, depth-ordered node list.
/// </summary>
internal sealed record CategoryTreeDto(IReadOnlyList<CategoryNodeDto> Nodes);

internal sealed record CategoryNodeDto(
    Guid CategoryId,
    string Name,
    string Path,
    Guid? ParentCategoryId,
    int Depth,
    int ProductCount);
