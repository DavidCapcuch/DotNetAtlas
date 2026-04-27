namespace Catalog.API.Endpoints.Categories.CreateCategory;

public sealed class CreateCategoryRequest
{
    public required string Name { get; set; }

    public Guid? ParentCategoryId { get; set; }
}
