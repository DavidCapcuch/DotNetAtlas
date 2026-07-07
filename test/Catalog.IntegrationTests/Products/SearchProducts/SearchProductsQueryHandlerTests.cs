using Catalog.Application.Common.FeatureFlags;
using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.SearchProducts;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using NSubstitute;
using OpenFeature;

namespace Catalog.IntegrationTests.Products.SearchProducts;

[Collection<IntegrationTestCollection>]
public sealed class SearchProductsQueryHandlerTests : BaseIntegrationTest
{
    public SearchProductsQueryHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    private static IFeatureClient FlagClient(bool showDiscontinued)
    {
        var client = Substitute.For<IFeatureClient>();
        client.GetBooleanValueAsync(
                CatalogFeatureFlags.ShowDiscontinuedInSearch,
                Arg.Any<bool>(),
                Arg.Any<OpenFeature.Model.EvaluationContext>(),
                Arg.Any<OpenFeature.Model.FlagEvaluationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(showDiscontinued));
        return client;
    }

    [Fact]
    public async Task Handle_FlagFalse_HidesDiscontinued()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(1);
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("ACT-1");
        }
    }

    [Fact]
    public async Task Handle_FlagTrue_IncludesDiscontinued()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: true));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(2);
            result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["ACT-1", "DIS-1"]);
        }
    }

    [Fact]
    public async Task Handle_ExplicitStatusFilter_FlagIgnored()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        // Flag is false, but query explicitly asks for Discontinued.
        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { Status = "Discontinued", PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(1);
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("DIS-1");
        }
    }

    [Fact]
    public async Task Handle_CategoryPathPrefix_FiltersByPrefix()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("A", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("B", categoryPath: "/books"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { CategoryPathPrefix = "/electronics", PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("A");
    }

    [Fact]
    public async Task Handle_CategoryPathPrefixSiblingSharesLeadingSubstring_SiblingIsExcluded()
    {
        // "/electronics" must match itself and its descendants, but NOT "/electronics-toys".
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("EXACT", categoryPath: "/electronics"),
            ProductSearchViewRowBuilder.Active("CHILD", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("SIBLING", categoryPath: "/electronics-toys"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { CategoryPathPrefix = "/electronics", PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        result.Should().BeSuccess();
        result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["EXACT", "CHILD"]);
    }

    [Fact]
    public async Task Handle_PriceRange_FiltersInclusive()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("LOW", amount: 1m),
            ProductSearchViewRowBuilder.Active("MID", amount: 5m),
            ProductSearchViewRowBuilder.Active("HIGH", amount: 10m));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery
            {
                MinPrice = 2m,
                MaxPrice = 7m,
                Currency = "USD",
                PageNumber = 1,
                PageSize = 10,
            },
            ct);

        // Assert
        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("MID");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_TextContainsLiteralPercent_PercentIsNotTreatedAsWildcard()
    {
        // Per CAT-SEC-001 / CAT-RV-H03: user-supplied "%" must be a literal, not a wildcard.
        // Three rows differ only by name; query Text="a%b" must match only the row with that
        // literal substring, not the broader rows that "%a%b%" would otherwise gobble up.
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "PCT-1", name: "a%b"),
            ProductSearchViewRowBuilder.Active(sku: "PCT-2", name: "aXb"),
            ProductSearchViewRowBuilder.Active(sku: "PCT-3", name: "abc"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { Text = "a%b", PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("PCT-1");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_TextContainsLiteralUnderscore_UnderscoreIsNotTreatedAsWildcard()
    {
        // Per CAT-SEC-001 / CAT-RV-H03: user-supplied "_" must be a literal, not a single-char wildcard.
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "UND-1", name: "a_b"),
            ProductSearchViewRowBuilder.Active(sku: "UND-2", name: "aXb"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { Text = "a_b", PageNumber = 1, PageSize = 10 }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("UND-1");
        }
    }

    [Fact]
    public async Task Handle_Paging_ReturnsCorrectSliceAndTotal()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var rows = new List<ProductSearchViewRow>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add(ProductSearchViewRowBuilder.Active($"SKU-{i}", amount: i + 1));
        }

        await seeder.SeedRowsAsync(ct, rows.ToArray());

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        // Act
        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 2, PageSize = 2 }, ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(5);
            // Pin the exact slice, not just its size: rows are ordered by PriceAmount
            // (seeded amount = i + 1) then ProductId, so page 2 / size 2 is deterministically
            // SKU-2, SKU-3. A wrong-offset mutation that still returns 2 rows fails here.
            result.Value.Items.Select(i => i.Sku).Should().Equal("SKU-2", "SKU-3");
        }
    }
}
