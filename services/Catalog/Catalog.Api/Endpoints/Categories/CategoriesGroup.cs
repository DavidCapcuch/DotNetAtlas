using FastEndpoints;

namespace Catalog.Api.Endpoints.Categories;

internal sealed class CategoriesGroup : Group
{
    public CategoriesGroup()
    {
        Configure("/catalog/categories", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName("Catalog.Categories"));
            ep.Tags("Catalog.Categories");
        });
    }
}
