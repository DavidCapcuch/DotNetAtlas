using Catalog.Application.Products.CreateProduct;

namespace Catalog.Api.Endpoints.Products.UpdateProductPrice;

public sealed class UpdateProductPriceRequest
{
    public Guid Id { get; set; }

    public required MoneyDto NewPrice { get; set; }
}
