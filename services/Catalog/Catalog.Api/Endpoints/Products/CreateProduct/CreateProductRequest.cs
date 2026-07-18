using Catalog.Application.Common.Contracts;

namespace Catalog.Api.Endpoints.Products.CreateProduct;

/// <summary>HTTP body for <see cref="CreateProductEndpoint"/>; mirrors <see cref="Catalog.Application.Products.CreateProduct.CreateProductCommand"/>.</summary>
public sealed class CreateProductRequest
{
    public required string Sku { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public required Guid CategoryId { get; set; }

    public required string Brand { get; set; }

    public required MoneyDto Price { get; set; }

    public DimensionsDto? Dimensions { get; set; }

    public required IReadOnlyList<ImageReferenceDto> Images { get; set; }
}
