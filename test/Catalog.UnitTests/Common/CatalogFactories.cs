using Catalog.Domain.Categories;
using Catalog.Domain.Categories.ValueObjects;
using Catalog.Domain.Products;
using Catalog.Domain.Products.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.UnitTests.Common;

/// <summary>
/// Shared test-data builders for Catalog unit tests. Keep aggregates in valid states; callers
/// tweak single fields where they need a specific scenario.
/// </summary>
public static class CatalogFactories
{
    public static Category RootCategory(string name = "Electronics")
    {
        return Category.Create(name, parentCategoryId: null, parentPath: null).Value;
    }

    public static Category ChildCategory(Category parent, string name = "Laptops")
    {
        return Category.Create(name, parent.Id, parent.Path).Value;
    }

    /// <summary>
    /// Creates a <see cref="Product"/> in the <c>Draft</c> status with sensible defaults.
    /// Tests that want a specific status should chain a state-transition call
    /// (<c>product.Activate(); product.Discontinue("reason");</c>).
    /// </summary>
    public static Product DraftProduct(
        Category? category = null,
        string sku = "TEST-001",
        string name = "Test Product",
        string description = "Test description",
        string brand = "TestBrand",
        decimal amount = 9.99m,
        string currency = "USD")
    {
        category ??= RootCategory();

        return Product.Create(
            Sku.Create(sku).Value,
            ProductName.Create(name).Value,
            ProductDescription.Create(description).Value,
            category.Id,
            BrandName.Create(brand).Value,
            Money.Create(amount, currency).Value,
            dimensions: null,
            images: []).Value;
    }

    public static Product ActiveProduct(Category? category = null, string sku = "TEST-001")
    {
        var product = DraftProduct(category, sku);
        product.Activate();
        product.PopDomainEvents();
        return product;
    }

    public static Product DiscontinuedProduct(Category? category = null, string sku = "TEST-001")
    {
        var product = ActiveProduct(category, sku);
        product.Discontinue("Discontinued for test");
        product.PopDomainEvents();
        return product;
    }
}
