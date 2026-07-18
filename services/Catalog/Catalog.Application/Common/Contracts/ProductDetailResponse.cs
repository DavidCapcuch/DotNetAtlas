namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Denormalized product-detail view returned by the single-product read (<c>GetProductById</c>) and,
/// as the element type, by the batch read (<c>GetProductsByIds</c>). Lives in
/// <c>Common.Contracts</c> so neither slice owns a type the other depends on.
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
