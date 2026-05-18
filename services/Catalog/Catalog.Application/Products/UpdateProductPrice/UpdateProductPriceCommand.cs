using Catalog.Application.Products.CreateProduct;
using Platform.CQRS;

namespace Catalog.Application.Products.UpdateProductPrice;

/// <summary>
/// Admin command to update a product's price. No-op when the new price matches the current one;
/// 409 when the product is <c>Discontinued</c>.
/// </summary>
public sealed record UpdateProductPriceCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required MoneyDto NewPrice { get; init; }
}
