namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Physical dimensions on the wire, shared by the create and read product slices.
/// Share/duplicate ruling: ADR-0037 § Implementation Notes.
/// </summary>
/// <remarks>
/// Purely a wire type. <c>product_search_view</c> stores dimensions as the four <c>dimensions_*</c>
/// scalar columns, mirroring the write model, so no edit here can reinterpret a stored row.
/// </remarks>
public sealed record DimensionsDto
{
    public required decimal Length { get; init; }

    public required decimal Width { get; init; }

    public required decimal Height { get; init; }

    public required string Unit { get; init; }
}
