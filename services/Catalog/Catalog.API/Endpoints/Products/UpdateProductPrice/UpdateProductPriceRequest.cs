using Catalog.Application.Products.CreateProduct;

namespace Catalog.API.Endpoints.Products.UpdateProductPrice;

public sealed class UpdateProductPriceRequest
{
    public Guid Id { get; set; }

    public required MoneyDto NewPrice { get; set; }
}
