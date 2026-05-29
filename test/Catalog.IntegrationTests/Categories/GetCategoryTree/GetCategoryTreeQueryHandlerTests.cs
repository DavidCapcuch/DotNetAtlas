using Catalog.Application.Categories.GetCategoryTree;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.IntegrationTests.Categories.GetCategoryTree;

[Collection<IntegrationTestCollection>]
public sealed class GetCategoryTreeQueryHandlerTests : BaseIntegrationTest
{
    public GetCategoryTreeQueryHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Given_NoRoot_When_Querying_Then_ReturnsFullTreeWithActiveProductCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var root = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        var laptops = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(root, "Laptops"), ct);

        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(categoryId: laptops.Id, categoryPath: laptops.Path.Value),
            ProductSearchViewRowBuilder.Active(categoryId: laptops.Id, categoryPath: laptops.Path.Value, sku: "ACT-2"),
            ProductSearchViewRowBuilder.Discontinued(categoryId: laptops.Id, categoryPath: laptops.Path.Value));

        var handler = new GetCategoryTreeQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(new GetCategoryTreeQuery(), ct);

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
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var rootA = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Books"), ct);
        var child = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(rootA, "Laptops"), ct);

        var handler = new GetCategoryTreeQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(new GetCategoryTreeQuery { RootCategoryId = rootA.Id }, ct);

        result.Should().BeSuccess();
        result.Value.Nodes.Select(n => n.CategoryId).Should().BeEquivalentTo([rootA.Id, child.Id]);
    }

    [Fact]
    public async Task Given_RootFilter_When_SiblingSharesLeadingSubstring_Then_SiblingIsExcluded()
    {
        // Root "/electronics" must match itself and its descendants, but NOT the sibling
        // "/electronics-toys" whose raw path shares the leading substring.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var electronics = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics Toys"), ct);
        var laptops = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(electronics, "Laptops"), ct);

        var handler = new GetCategoryTreeQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(new GetCategoryTreeQuery { RootCategoryId = electronics.Id }, ct);

        result.Should().BeSuccess();
        result.Value.Nodes.Select(n => n.CategoryId)
            .Should().BeEquivalentTo([electronics.Id, laptops.Id]);
    }

    [Fact]
    public async Task Given_UnknownRoot_When_Querying_Then_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new GetCategoryTreeQueryHandler(CatalogDbContext);

        var result = await handler.HandleAsync(
            new GetCategoryTreeQuery { RootCategoryId = Guid.CreateVersion7() }, ct);

        result.Should().BeSuccess();
        result.Value.Nodes.Should().BeEmpty();
    }
}
