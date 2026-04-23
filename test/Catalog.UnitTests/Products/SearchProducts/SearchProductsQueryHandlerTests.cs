using Catalog.Application.Common.FeatureFlags;
using Catalog.Application.Products.SearchProducts;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using NSubstitute;
using OpenFeature;

namespace Catalog.UnitTests.Products.SearchProducts;

public class SearchProductsQueryHandlerTests
{
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
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.AddRange(
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

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
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.AddRange(
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: true));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

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
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.AddRange(
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Flag is false, but query explicitly asks for Discontinued.
        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { Status = "Discontinued", PageNumber = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

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
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.AddRange(
            ProductSearchViewRowBuilder.Active("A", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("B", categoryPath: "/books"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { CategoryPathPrefix = "/electronics", PageNumber = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("A");
    }

    [Fact]
    public async Task Given_CategoryPathPrefix_When_SiblingSharesLeadingSubstring_Then_SiblingIsExcluded()
    {
        // "/electronics" must match itself and its descendants, but NOT "/electronics-toys".
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.AddRange(
            ProductSearchViewRowBuilder.Active("EXACT", categoryPath: "/electronics"),
            ProductSearchViewRowBuilder.Active("CHILD", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("SIBLING", categoryPath: "/electronics-toys"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { CategoryPathPrefix = "/electronics", PageNumber = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Items.Select(i => i.Sku).Should().BeEquivalentTo(["EXACT", "CHILD"]);
    }

    [Fact]
    public async Task Given_PriceRange_When_Searching_Then_FiltersInclusive()
    {
        await using var db = FakeCatalogDbContext.Create();
        db.ProductSearchView.AddRange(
            ProductSearchViewRowBuilder.Active("LOW", amount: 1m),
            ProductSearchViewRowBuilder.Active("MID", amount: 5m),
            ProductSearchViewRowBuilder.Active("HIGH", amount: 10m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery
            {
                MinPrice = 2m,
                MaxPrice = 7m,
                Currency = "USD",
                PageNumber = 1,
                PageSize = 10,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Items.Should().ContainSingle().Which.Sku.Should().Be("MID");
    }

    [Fact]
    public async Task Given_Paging_When_Searching_Then_ReturnsCorrectSliceAndTotal()
    {
        await using var db = FakeCatalogDbContext.Create();
        for (var i = 0; i < 5; i++)
        {
            db.ProductSearchView.Add(ProductSearchViewRowBuilder.Active($"SKU-{i}", amount: i + 1));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new SearchProductsQueryHandler(db, FlagClient(showDiscontinued: false));

        var result = await handler.HandleAsync(
            new SearchProductsQuery { PageNumber = 2, PageSize = 2 },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Total.Should().Be(5);
            result.Value.Items.Should().HaveCount(2);
        }
    }
}
