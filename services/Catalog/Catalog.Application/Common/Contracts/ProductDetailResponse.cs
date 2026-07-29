namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Denormalized product-detail view carried as the element type of the batch read
/// (<c>GetProductsByIds</c>). An envelope's item type declared outside its slice is an outstanding
/// ADR-0037 violation, tracked in #353.
/// </summary>
public sealed class ProductDetailResponse
{
    public required Guid ProductId { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public required Guid CategoryId { get; set; }

    public required string CategoryPath { get; set; }

    public required string CategoryBreadcrumb { get; set; }

    public required string BrandName { get; set; }

    public required MoneyDto Price { get; set; }

    public required string Status { get; set; }

    public DimensionsDto? Dimensions { get; set; }

    public required IReadOnlyList<ImageReferenceDto> Images { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; set; }

    public required DateTimeOffset LastUpdatedAtUtc { get; set; }
}
