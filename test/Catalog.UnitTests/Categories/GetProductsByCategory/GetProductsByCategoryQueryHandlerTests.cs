using Catalog.Application.Categories.GetProductsByCategory;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.UnitTests.Categories.GetProductsByCategory;

public class GetProductsByCategoryQueryHandlerTests
{
    [Fact]
    public async Task Given_IncludeDescendantsFalse_When_Querying_Then_MatchesCategoryIdOnly()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory("Electronics");
        db.Categories.Add(category);

        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(
            sku: "MATCH", categoryId: category.Id, categoryPath: category.Path.Value));
        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(sku: "OTHER"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetProductsByCategoryQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = category.Id,
                IncludeDescendants = false,
                PageNumber = 1,
                PageSize = 10,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("MATCH");
        }
    }

    [Fact]
    public async Task Given_IncludeDescendantsTrue_When_Querying_Then_MatchesPathPrefix()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var child = CatalogFactories.ChildCategory(root, "Laptops");
        db.Categories.AddRange(root, child);

        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(
            sku: "ROOT", categoryId: root.Id, categoryPath: root.Path.Value));
        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(
            sku: "CHILD", categoryId: child.Id, categoryPath: child.Path.Value));
        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(
            sku: "OTHER", categoryPath: "/books"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetProductsByCategoryQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = root.Id,
                IncludeDescendants = true,
                PageNumber = 1,
                PageSize = 10,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["ROOT", "CHILD"]);
        }
    }

    [Fact]
    public async Task Given_UnknownCategoryWithDescendants_Then_ReturnsEmptyPage()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new GetProductsByCategoryQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = Guid.CreateVersion7(),
                IncludeDescendants = true,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Items.Should().BeEmpty();
    }
}
