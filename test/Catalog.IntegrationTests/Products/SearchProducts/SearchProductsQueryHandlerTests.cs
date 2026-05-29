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
    public async Task Given_FlagFalse_When_Searching_Then_HidesDiscontinued()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 1, PageSize = 10 }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(1);
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("ACT-1");
        }
    }

    [Fact]
    public async Task Given_FlagTrue_When_Searching_Then_IncludesDiscontinued()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: true));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 1, PageSize = 10 }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(2);
            result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["ACT-1", "DIS-1"]);
        }
    }

    [Fact]
    public async Task Given_ExplicitStatusFilter_When_Searching_Then_FlagIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        // Flag is false, but query explicitly asks for Discontinued.
        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { Status = "Discontinued", PageNumber = 1, PageSize = 10 }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(1);
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("DIS-1");
        }
    }

    [Fact]
    public async Task Given_CategoryPathPrefix_When_Searching_Then_FiltersByPrefix()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("A", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("B", categoryPath: "/books"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { CategoryPathPrefix = "/electronics", PageNumber = 1, PageSize = 10 }, ct);

        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("A");
    }

    [Fact]
    public async Task Given_CategoryPathPrefix_When_SiblingSharesLeadingSubstring_Then_SiblingIsExcluded()
    {
        // "/electronics" must match itself and its descendants, but NOT "/electronics-toys".
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("EXACT", categoryPath: "/electronics"),
            ProductSearchViewRowBuilder.Active("CHILD", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("SIBLING", categoryPath: "/electronics-toys"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { CategoryPathPrefix = "/electronics", PageNumber = 1, PageSize = 10 }, ct);

        result.Should().BeSuccess();
        result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["EXACT", "CHILD"]);
    }

    [Fact]
    public async Task Given_PriceRange_When_Searching_Then_FiltersInclusive()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active("LOW", amount: 1m),
            ProductSearchViewRowBuilder.Active("MID", amount: 5m),
            ProductSearchViewRowBuilder.Active("HIGH", amount: 10m));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

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

        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("MID");
    }

    [Fact]
    public async Task Given_TextContainsLiteralPercent_When_Searching_Then_PercentIsNotTreatedAsWildcard()
    {
        // Per CAT-SEC-001 / CAT-RV-H03: user-supplied "%" must be a literal, not a wildcard.
        // Three rows differ only by name; query Text="a%b" must match only the row with that
        // literal substring, not the broader rows that "%a%b%" would otherwise gobble up.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "PCT-1", name: "a%b"),
            ProductSearchViewRowBuilder.Active(sku: "PCT-2", name: "aXb"),
            ProductSearchViewRowBuilder.Active(sku: "PCT-3", name: "abc"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { Text = "a%b", PageNumber = 1, PageSize = 10 }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("PCT-1");
        }
    }

    [Fact]
    public async Task Given_TextContainsLiteralUnderscore_When_Searching_Then_UnderscoreIsNotTreatedAsWildcard()
    {
        // Per CAT-SEC-001 / CAT-RV-H03: user-supplied "_" must be a literal, not a single-char wildcard.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        await seeder.SeedRowsAsync(
            ct,
            ProductSearchViewRowBuilder.Active(sku: "UND-1", name: "a_b"),
            ProductSearchViewRowBuilder.Active(sku: "UND-2", name: "aXb"));

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { Text = "a_b", PageNumber = 1, PageSize = 10 }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("UND-1");
        }
    }

    [Fact]
    public async Task Given_Paging_When_Searching_Then_ReturnsCorrectSliceAndTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(CatalogDbContext);
        var rows = new List<ProductSearchViewRow>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add(ProductSearchViewRowBuilder.Active($"SKU-{i}", amount: i + 1));
        }

        await seeder.SeedRowsAsync(ct, rows.ToArray());

        var handler = new SearchProductsQueryHandler(CatalogDbContext, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 2, PageSize = 2 }, ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(5);
            result.Value.Items.Should().HaveCount(2);
        }
    }
}
