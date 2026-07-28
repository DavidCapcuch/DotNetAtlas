namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Physical dimensions on the wire, shared by the create, read, and projection product slices.
/// </summary>
/// <remarks>
/// Also the serialized shape of the <c>dimensions_json</c> column on <c>product_search_view</c>
/// (<see cref="Catalog.Application.Common.ReadModels.ProductSearchViewMapper"/>), so renaming or
/// retyping a member reinterprets stored rows and needs a projection rebuild — not only a consumer
/// update.
/// </remarks>
public sealed record DimensionsDto
{
    public required decimal Length { get; init; }

    public required decimal Width { get; init; }

    public required decimal Height { get; init; }

    public required string Unit { get; init; }
}
