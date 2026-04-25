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
/// <remarks>
/// All factory helpers default <c>utcNow</c> to <see cref="DefaultUtcNow"/> so unit tests that
/// don't care about <c>OccurredOnUtc</c> are unaffected by the M4.3 H1 signature change. Tests
/// that DO care pass an explicit value to assert event timestamps deterministically (typically
/// the same static instant they later compare against).
/// </remarks>
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

    /// <summary>
    /// Creates a <see cref="Product"/> in the <c>Draft</c> status with sensible defaults.
    /// Tests that want a specific status should chain a state-transition call
    /// (<c>product.Activate(utcNow); product.Discontinue("reason", utcNow);</c>).
    /// </summary>
    public static Product DraftProduct(
        Category? category = null,
        string sku = "TEST-001",
        string name = "Test Product",
        string description = "Test description",
        string brand = "TestBrand",
        decimal amount = 9.99m,
        string currency = "USD",
        DateTimeOffset? utcNow = null)
    {
        category ??= RootCategory(utcNow: utcNow);

        return Product.Create(
            Sku.Create(sku).Value,
            ProductName.Create(name).Value,
            ProductDescription.Create(description).Value,
            category.Id,
            BrandName.Create(brand).Value,
            Money.Create(amount, currency).Value,
            dimensions: null,
            images: [],
            utcNow ?? DefaultUtcNow).Value;
    }

    public static Product ActiveProduct(Category? category = null, string sku = "TEST-001", DateTimeOffset? utcNow = null)
    {
        var product = DraftProduct(category, sku, utcNow: utcNow);
        product.Activate(utcNow ?? DefaultUtcNow);
        product.PopDomainEvents();
        return product;
    }

    public static Product DiscontinuedProduct(Category? category = null, string sku = "TEST-001", DateTimeOffset? utcNow = null)
    {
        var product = ActiveProduct(category, sku, utcNow);
        product.Discontinue("Discontinued for test", utcNow ?? DefaultUtcNow);
        product.PopDomainEvents();
        return product;
    }
}
