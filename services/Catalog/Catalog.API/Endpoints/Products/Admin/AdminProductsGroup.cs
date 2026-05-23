using FastEndpoints;

namespace Catalog.API.Endpoints.Products.Admin;

/// <summary>
/// FastEndpoints route group for admin-only product operations. Mounted under
/// <c>/catalog/admin/products</c> so admin tooling can reach Discontinued products without
/// flipping the <c>catalog.show-discontinued-in-search</c> feature flag (ADR-0014) for
/// everyone (#172). All endpoints in this group must require <c>catalog.write</c> scope.
/// </summary>
internal sealed class AdminProductsGroup : Group
{
    public AdminProductsGroup()
    {
        Configure("/catalog/admin/products", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName("Catalog.Products.Admin"));
            ep.Tags("Catalog.Products.Admin");
        });
    }
}
