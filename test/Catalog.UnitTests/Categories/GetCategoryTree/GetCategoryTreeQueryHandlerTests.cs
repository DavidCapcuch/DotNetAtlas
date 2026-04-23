using Catalog.Application.Categories.GetCategoryTree;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.UnitTests.Categories.GetCategoryTree;

public class GetCategoryTreeQueryHandlerTests
{
    [Fact]
    public async Task Given_NoRoot_When_Querying_Then_ReturnsFullTreeWithActiveProductCounts()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory("Electronics");
        var laptops = CatalogFactories.ChildCategory(root, "Laptops");
        db.Categories.AddRange(root, laptops);

        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(categoryId: laptops.Id, categoryPath: laptops.Path.Value));
        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active(categoryId: laptops.Id, categoryPath: laptops.Path.Value, sku: "ACT-2"));
        db.ProductSearchView.Add(ProductSearchViewRowBuilder.Discontinued(categoryId: laptops.Id, categoryPath: laptops.Path.Value));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetCategoryTreeQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetCategoryTreeQuery(),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Nodes.Should().HaveCount(2);
            result.Value.Nodes.Single(n => n.CategoryId == laptops.Id).ProductCount.Should().Be(2);
            result.Value.Nodes.Single(n => n.CategoryId == root.Id).Depth.Should().Be(1);
            result.Value.Nodes.Single(n => n.CategoryId == laptops.Id).Depth.Should().Be(2);
        }
    }

    [Fact]
    public async Task Given_RootFilter_When_Querying_Then_ReturnsOnlySubtree()
    {
        await using var db = FakeCatalogDbContext.Create();
        var rootA = CatalogFactories.RootCategory("Electronics");
        var rootB = CatalogFactories.RootCategory("Books");
        var child = CatalogFactories.ChildCategory(rootA, "Laptops");
        db.Categories.AddRange(rootA, rootB, child);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetCategoryTreeQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetCategoryTreeQuery { RootCategoryId = rootA.Id },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Nodes.Select(n => n.CategoryId).Should().BeEquivalentTo([rootA.Id, child.Id]);
    }

    [Fact]
    public async Task Given_RootFilter_When_SiblingSharesLeadingSubstring_Then_SiblingIsExcluded()
    {
        // Root "/electronics" must match itself and its descendants, but NOT the sibling
        // "/electronics-toys" whose raw path shares the leading substring.
        await using var db = FakeCatalogDbContext.Create();
        var electronics = CatalogFactories.RootCategory("Electronics");
        var electronicsToys = CatalogFactories.RootCategory("Electronics Toys");
        var laptops = CatalogFactories.ChildCategory(electronics, "Laptops");
        db.Categories.AddRange(electronics, electronicsToys, laptops);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetCategoryTreeQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetCategoryTreeQuery { RootCategoryId = electronics.Id },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Nodes.Select(n => n.CategoryId)
            .Should().BeEquivalentTo([electronics.Id, laptops.Id]);
    }

    [Fact]
    public async Task Given_UnknownRoot_When_Querying_Then_ReturnsEmpty()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new GetCategoryTreeQueryHandler(db);

        var result = await handler.HandleAsync(
            new GetCategoryTreeQuery { RootCategoryId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Nodes.Should().BeEmpty();
    }
}
