namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Physical dimensions on the wire, shared by the create and read product slices. Lives in
/// <c>Common.Contracts</c> so no feature slice owns a type its siblings depend on.
/// </summary>
public sealed record DimensionsDto
{
    public required decimal Length { get; init; }

    public required decimal Width { get; init; }

    public required decimal Height { get; init; }

    public required string Unit { get; init; }
}
