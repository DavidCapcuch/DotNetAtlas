namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Product image reference on the wire, shared by the create and read product slices.
/// Share/duplicate ruling: ADR-0037 § Implementation Notes.
/// </summary>
/// <remarks>
/// Purely a wire type. The stored shape of the <c>images_json</c> column is the separate
/// <see cref="Catalog.Application.Common.ReadModels.ProductImageDocument"/>, so no edit here can
/// reinterpret a stored row.
/// </remarks>
public sealed record ImageReferenceDto
{
    public required string Url { get; init; }

    public required string AltText { get; init; }

    public required int DisplayOrder { get; init; }
}
