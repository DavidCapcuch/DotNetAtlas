using Microsoft.EntityFrameworkCore;

namespace Catalog.UnitTests.Common;

public class FakeCatalogDbContextSmokeTests
{
    [Fact]
    public async Task CanAddAndRoundTripProductAndCategoryAcrossSaveChanges()
    {
        await using var db = FakeCatalogDbContext.Create();

        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var roundTrippedCategory = await db.Categories.FirstAsync(
            c => c.Id == category.Id, TestContext.Current.CancellationToken);
        var roundTrippedProduct = await db.Products.FirstAsync(
            p => p.Id == product.Id, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            roundTrippedCategory.Name.Should().Be(category.Name);
            roundTrippedProduct.Sku.Value.Should().Be("TEST-001");
            roundTrippedProduct.Price.Amount.Should().Be(9.99m);
            roundTrippedProduct.Price.Currency.Name.Should().Be("USD");
            roundTrippedProduct.Status.Name.Should().Be("Active");
        }
    }
}
