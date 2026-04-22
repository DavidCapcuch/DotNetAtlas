using System.Collections.Generic;
using Platform.CQRS;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Admin command to create a new <see cref="Catalog.Domain.Products.Product"/> in
/// <see cref="Catalog.Domain.Products.ValueObjects.ProductStatus.Draft"/> status.
/// Returns the new product's identity on success.
/// </summary>
public class CreateProductCommand : ICommand<Guid>
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

public sealed class MoneyDto
{
    public required decimal Amount { get; set; }

    public required string Currency { get; set; }
}

public sealed class DimensionsDto
{
    public required decimal Length { get; set; }

    public required decimal Width { get; set; }

    public required decimal Height { get; set; }

    public required string Unit { get; set; }
}

public sealed class ImageReferenceDto
{
    public required string Url { get; set; }

    public required string AltText { get; set; }

    public required int DisplayOrder { get; set; }
}
