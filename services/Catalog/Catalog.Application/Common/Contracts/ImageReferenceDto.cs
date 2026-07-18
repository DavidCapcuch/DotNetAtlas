namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Product image reference on the wire, shared by the create, read, and projection slices. Lives in
/// <c>Common.Contracts</c> so no feature slice owns a type its siblings depend on.
/// </summary>
public sealed record ImageReferenceDto
{
    public required string Url { get; init; }

    public required string AltText { get; init; }

    public required int DisplayOrder { get; init; }
}
