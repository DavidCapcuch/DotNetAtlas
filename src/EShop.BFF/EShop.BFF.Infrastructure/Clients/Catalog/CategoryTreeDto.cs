namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// BFF-internal re-declaration of Catalog's category tree (anti-corruption, bff.md § 4.1). Mirrors
/// Catalog's <c>GET /api/v1/catalog/categories/tree</c> response — a flat, depth-ordered node list.
/// </summary>
internal sealed record CategoryTreeDto
{
    public required IReadOnlyList<CategoryNodeDto> Nodes { get; init; }
}

internal sealed record CategoryNodeDto
{
    public required Guid CategoryId { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    /// <summary><c>null</c> upstream for a root category.</summary>
    public Guid? ParentCategoryId { get; init; }

    public required int Depth { get; init; }

    public required int ProductCount { get; init; }
}
