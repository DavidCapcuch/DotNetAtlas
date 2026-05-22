using System.Collections.Generic;
using Platform.CQRS;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Admin command to create a new <see cref="Catalog.Domain.Products.Product"/> in
/// <see cref="Catalog.Domain.Products.ValueObjects.ProductStatus.Active"/> status.
/// Returns the new product's identity on success.
/// </summary>
public sealed record CreateProductCommand : ICommand<Guid>
{
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required Guid CategoryId { get; init; }

    public required string Brand { get; init; }

    public required MoneyDto Price { get; init; }

    public DimensionsDto? Dimensions { get; init; }

    public required IReadOnlyList<ImageReferenceDto> Images { get; init; }
}

public sealed record MoneyDto
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

public sealed record DimensionsDto
{
    public required decimal Length { get; init; }

    public required decimal Width { get; init; }

    public required decimal Height { get; init; }

    public required string Unit { get; init; }
}

public sealed record ImageReferenceDto
{
    public required string Url { get; init; }

    public required string AltText { get; init; }

    public required int DisplayOrder { get; init; }
}
