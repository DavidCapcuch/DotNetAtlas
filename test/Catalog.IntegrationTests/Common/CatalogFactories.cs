using Catalog.Domain.Categories;

namespace Catalog.IntegrationTests.Common;

/// <summary>
/// Category builders for the migrated read-projection integration tests. Mirrors the unit-tier
/// <c>CatalogFactories</c> (kept as a separate copy so the integration suite carries no dependency
/// on the unit-test project). Only the two category helpers these tests need are reproduced here.
/// </summary>
public static class CatalogFactories
{
    /// <summary>Static instant used as the default <c>utcNow</c> for every factory helper.</summary>
    public static readonly DateTimeOffset DefaultUtcNow =
        new(2026, 4, 25, 12, 0, 0, TimeSpan.Zero);

    public static Category RootCategory(string name = "Electronics", DateTimeOffset? utcNow = null)
    {
        return Category.Create(name, parentCategoryId: null, parentPath: null, utcNow ?? DefaultUtcNow).Value;
    }

    public static Category ChildCategory(Category parent, string name = "Laptops", DateTimeOffset? utcNow = null)
    {
        return Category.Create(name, parent.Id, parent.Path, utcNow ?? DefaultUtcNow).Value;
    }
}
