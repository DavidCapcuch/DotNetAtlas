namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Product image reference on the wire, shared by the create, read, and projection slices.
/// </summary>
/// <remarks>
/// Also the serialized shape of the <c>images_json</c> column on <c>product_search_view</c>
/// (<see cref="Catalog.Application.Common.ReadModels.ProductSearchViewMapper"/>), so renaming or
/// retyping a member reinterprets stored rows and needs a projection rebuild — not only a consumer
/// update.
/// </remarks>
public sealed record ImageReferenceDto
{
    public required string Url { get; init; }

    public required string AltText { get; init; }

    public required int DisplayOrder { get; init; }
}
