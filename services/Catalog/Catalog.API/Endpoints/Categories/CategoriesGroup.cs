using FastEndpoints;

namespace Catalog.API.Endpoints.Categories;

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
