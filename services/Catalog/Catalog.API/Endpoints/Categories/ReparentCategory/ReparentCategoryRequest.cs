namespace Catalog.API.Endpoints.Categories.ReparentCategory;

public sealed class ReparentCategoryRequest
{
    public Guid Id { get; set; }

    public Guid? NewParentCategoryId { get; set; }
}
