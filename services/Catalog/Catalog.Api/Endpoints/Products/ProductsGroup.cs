using FastEndpoints;

namespace Catalog.Api.Endpoints.Products;

internal sealed class ProductsGroup : Group
{
    public ProductsGroup()
    {
        Configure("/catalog/products", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName("Catalog.Products"));
            ep.Tags("Catalog.Products");
        });
    }
}
