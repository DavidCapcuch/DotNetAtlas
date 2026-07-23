using Catalog.Domain.Categories;
using AvroCategoryCreatedEvent = Catalog.Categories.CategoryCreatedEvent;

namespace Catalog.Application.Categories.CreateCategory;

/// <summary>
/// Maps the <see cref="Category"/> aggregate to the external Avro
/// <see cref="AvroCategoryCreatedEvent"/>. 1:1 projection of the persisted node.
/// </summary>
internal static class CategoryCreatedMapper
{
    public static AvroCategoryCreatedEvent ToCategoryCreatedEvent(this Category category) =>
        new()
        {
            CategoryId = category.Id,
            Name = category.Name,
            ParentCategoryId = category.ParentCategoryId,
            Path = category.Path.Value,
            CreatedAtUtc = category.CreatedUtc.UtcDateTime,
        };
}
