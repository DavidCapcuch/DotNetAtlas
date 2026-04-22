using Catalog.Application.Products.CreateProduct;

namespace Catalog.Application.Products.GetProductById;

/// <summary>Response DTO for <see cref="GetProductByIdQuery"/>.</summary>
public sealed class GetProductByIdResponse
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
