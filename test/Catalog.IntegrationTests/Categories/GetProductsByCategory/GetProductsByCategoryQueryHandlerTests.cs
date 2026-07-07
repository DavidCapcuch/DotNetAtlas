using Catalog.Application.Categories.GetProductsByCategory;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;

namespace Catalog.IntegrationTests.Categories.GetProductsByCategory;

[Collection<IntegrationTestCollection>]
public sealed class GetProductsByCategoryQueryHandlerTests : BaseIntegrationTest
{
    public GetProductsByCategoryQueryHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Handle_IncludeDescendantsFalse_MatchesCategoryIdOnly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var category = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);

        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "MATCH", categoryId: category.Id, categoryPath: category.Path.Value),
            ProductSearchViewRowBuilder.Active(sku: "OTHER"));

        var handler = new GetProductsByCategoryQueryHandler(CatalogDbContext);

        // Act
        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = category.Id,
                IncludeDescendants = false,
                PageNumber = 1,
                PageSize = 10,
            },
            ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("MATCH");
        }
    }

    [Fact]
    public async Task Handle_IncludeDescendantsTrue_MatchesPathPrefix()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var root = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);
        var child = await seeder.SeedCategoryAsync(CatalogFactories.ChildCategory(root, "Laptops"), ct);

        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "ROOT", categoryId: root.Id, categoryPath: root.Path.Value),
            ProductSearchViewRowBuilder.Active(sku: "CHILD", categoryId: child.Id, categoryPath: child.Path.Value),
            ProductSearchViewRowBuilder.Active(sku: "OTHER", categoryPath: "/books"));

        var handler = new GetProductsByCategoryQueryHandler(CatalogDbContext);

        // Act
        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = root.Id,
                IncludeDescendants = true,
                PageNumber = 1,
                PageSize = 10,
            },
            ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["ROOT", "CHILD"]);
        }
    }

    [Fact]
    public async Task Handle_IncludeDescendantsTrueSiblingSharesLeadingSubstring_SiblingIsExcluded()
    {
        // Root "/electronics" must match itself and its descendants, but NOT the sibling
        // "/electronics-toys" whose raw path shares the leading substring.
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var root = await seeder.SeedCategoryAsync(CatalogFactories.RootCategory("Electronics"), ct);

        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "EXACT", categoryId: root.Id, categoryPath: root.Path.Value),
            ProductSearchViewRowBuilder.Active(sku: "CHILD", categoryPath: root.Path.Value + "/laptops"),
            ProductSearchViewRowBuilder.Active(sku: "SIBLING", categoryPath: root.Path.Value + "-toys"));

        var handler = new GetProductsByCategoryQueryHandler(CatalogDbContext);

        // Act
        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = root.Id,
                IncludeDescendants = true,
                PageNumber = 1,
                PageSize = 10,
            },
            ct);

        // Assert
        result.Should().BeSuccess();
        result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["EXACT", "CHILD"]);
    }

    [Fact]
    public async Task Handle_UnknownCategoryWithDescendants_ReturnsEmptyPage()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var handler = new GetProductsByCategoryQueryHandler(CatalogDbContext);

        // Act
        var result = await handler.HandleAsync(
            new GetProductsByCategoryQuery
            {
                CategoryId = Guid.CreateVersion7(),
                IncludeDescendants = true,
            },
            ct);

        // Assert
        result.Should().BeSuccess();
        result.Value.Items.Should().BeEmpty();
    }
}
