using System.Net;
using System.Net.Http.Json;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.DiscontinueProduct;
using Catalog.Application.Common.Contracts;
using Catalog.Application.Common.FeatureFlags;
using Catalog.Application.Common.ReadModels;
using Catalog.IntegrationTests.Common;
using FastEndpoints;
using NSubstitute;

namespace Catalog.IntegrationTests.ApiEndpoints.Products;

[Collection<IntegrationTestCollection>]
public class SearchProductsTests : BaseIntegrationTest
{
    public SearchProductsTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenStatusFilteredToActive_NewlyCreatedProductAppears()
    {
        // Post-#177: products are Active on create, so the default Active filter surfaces
        // them directly. Use raw HttpClient — FastEndpoints' GETAsync<TEndpoint,TRequest,
        // TResponse> emits a double-slash URL ("/api/v1/catalog/products//?…") for
        // endpoints with Get("").
        var categoryId = await SeedCategoryAsync();
        var (_, activeProduct) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(categoryId, name: "ActiveOne"));

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            "/api/v1/catalog/products?Status=Active&Page=1&Limit=50",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == activeProduct.ProductId);
        }
    }

    [Fact]
    public async Task WhenShowDiscontinuedFlagFlippedOnAtRuntime_DiscontinuedProductSurfacesInSearch()
    {
        // Verifies the catalog.show-discontinued-in-search flag changes search results without
        // restart (ADR-0014). The observable outcome: with the flag ON,
        // a discontinued product surfaces in the default (no explicit status filter) public
        // search — so a mutation that consults the flag but ignores its value (keeps hiding
        // discontinued rows) fails here, where the prior mock-only "was the flag read?" check
        // survived it. The Received() call stays as a secondary guard that the flag drives the path.
        // Arrange — create then discontinue a product; the default public search hides it unless
        // the flag flips it back on.
        var categoryId = await SeedCategoryAsync();
        var (_, product) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(categoryId, name: "Flag-Toggled"));
        await HttpClientRegistry.WriteClient
            .POSTAsync<DiscontinueProductEndpoint, DiscontinueProductRequest>(
                new DiscontinueProductRequest { Id = product.ProductId, Reason = "End-of-life" });

        FeatureClient.GetBooleanValueAsync(
                CatalogFeatureFlags.ShowDiscontinuedInSearch,
                Arg.Any<bool>(),
                Arg.Any<OpenFeature.Model.EvaluationContext?>(),
                Arg.Any<OpenFeature.Model.FlagEvaluationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        // Act — default search applies no explicit status filter, so the flag alone governs
        // whether the discontinued row is included.
        var response = await HttpClientRegistry.ReadClient.GetAsync(
            "/api/v1/catalog/products?Page=1&Limit=50",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == product.ProductId);
            await FeatureClient.Received().GetBooleanValueAsync(
                CatalogFeatureFlags.ShowDiscontinuedInSearch,
                Arg.Any<bool>(),
                Arg.Any<OpenFeature.Model.EvaluationContext?>(),
                Arg.Any<OpenFeature.Model.FlagEvaluationOptions?>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task WhenTextFilterApplied_NarrowsResults()
    {
        var categoryId = await SeedCategoryAsync();
        var (_, alpha) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(categoryId, name: "Alpha-Specific"));
        var (_, beta) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(categoryId, name: "Beta-Whatever"));

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            "/api/v1/catalog/products?Status=Active&Text=Alpha-Specific",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == alpha.ProductId);
            body.Items.Should().NotContain(i => i.ProductId == beta.ProductId);
        }
    }

    // The blocks below are folded from SearchProductsQueryHandlerTests: the SQL-filter branches now
    // enter through the public search endpoint (so query-param binding + validation are exercised too),
    // while the read-model rows are seeded directly for precise control. The default (no-status) search
    // hides discontinued rows unless the flag is on; the fixture's substitute defaults the flag to false.

    [Fact]
    public async Task WhenFlagFalse_DefaultSearch_HidesDiscontinued()
    {
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        var body = await SearchAsync("?Page=1&Limit=10");

        using (new AssertionScope())
        {
            body.Total.Should().Be(1);
            body.Items.Should().ContainSingle().Which.Sku.Should().Be("ACT-1");
        }
    }

    [Fact]
    public async Task WhenExplicitStatusDiscontinued_ReturnsDiscontinuedRegardlessOfFlag()
    {
        // Flag is false (default), but an explicit Status filter must override the hide-by-default.
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active("ACT-1", amount: 1m),
            ProductSearchViewRowBuilder.Discontinued("DIS-1"));

        var body = await SearchAsync("?Status=Discontinued&Page=1&Limit=10");

        using (new AssertionScope())
        {
            body.Total.Should().Be(1);
            body.Items.Should().ContainSingle().Which.Sku.Should().Be("DIS-1");
        }
    }

    [Fact]
    public async Task WhenCategoryPathPrefix_FiltersByPrefix()
    {
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active("A", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("B", categoryPath: "/books"));

        var body = await SearchAsync("?CategoryPath=/electronics&Page=1&Limit=10");

        body.Items.Should().ContainSingle().Which.Sku.Should().Be("A");
    }

    [Fact]
    public async Task WhenCategoryPathPrefixSiblingSharesLeadingSubstring_SiblingIsExcluded()
    {
        // "/electronics" must match itself and its descendants, but NOT "/electronics-toys".
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active("EXACT", categoryPath: "/electronics"),
            ProductSearchViewRowBuilder.Active("CHILD", categoryPath: "/electronics/laptops"),
            ProductSearchViewRowBuilder.Active("SIBLING", categoryPath: "/electronics-toys"));

        var body = await SearchAsync("?CategoryPath=/electronics&Page=1&Limit=10");

        body.Items.Select(i => i.Sku).Should().BeEquivalentTo(["EXACT", "CHILD"]);
    }

    [Fact]
    public async Task WhenProductHasImages_MapsMoneyAndPrimaryImageOntoTheResultItem()
    {
        // This slice owns its own row-to-wire mapper (ADR-0037), so a break here is invisible to
        // GetProductsByCategory's tests and vice versa. Images are seeded out of DisplayOrder so
        // the lowest-order pick is distinguishable from source order.
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active("MAP-001", name: "Mapped Widget", amount: 42.50m, currency: "GBP")
                .WithImages(
                    new ImageReferenceDto { Url = "https://cdn.test/secondary.png", AltText = "b", DisplayOrder = 3 },
                    new ImageReferenceDto { Url = "https://cdn.test/primary.png", AltText = "a", DisplayOrder = 1 }));

        var body = await SearchAsync("?Text=Mapped&Page=1&Limit=10");

        var item = body.Items.Should().ContainSingle().Which;
        using (new AssertionScope())
        {
            item.Sku.Should().Be("MAP-001");
            item.BrandName.Should().Be("Acme");
            item.Price.Amount.Should().Be(42.50m);
            item.Price.Currency.Should().Be("GBP");
            item.PrimaryImageUrl.Should().Be("https://cdn.test/primary.png");
        }
    }

    [Fact]
    public async Task WhenPriceRange_FiltersInclusive()
    {
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active("LOW", amount: 1m),
            ProductSearchViewRowBuilder.Active("MID", amount: 5m),
            ProductSearchViewRowBuilder.Active("HIGH", amount: 10m));

        var body = await SearchAsync("?MinPrice=2&MaxPrice=7&Currency=USD&Page=1&Limit=10");

        body.Items.Should().ContainSingle().Which.Sku.Should().Be("MID");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenTextContainsLiteralPercent_PercentIsNotTreatedAsWildcard()
    {
        // Per CAT-SEC-001 / CAT-RV-H03: a user-supplied "%" (URL-encoded %25) must be a literal, not a
        // wildcard. Only the row whose name literally contains "a%b" matches; a wildcard would also
        // gobble "aXb". The endpoint decodes %25 → % before the handler escapes it.
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active(sku: "PCT-1", name: "a%b"),
            ProductSearchViewRowBuilder.Active(sku: "PCT-2", name: "aXb"),
            ProductSearchViewRowBuilder.Active(sku: "PCT-3", name: "abc"));

        var body = await SearchAsync("?Text=a%25b&Page=1&Limit=10");

        body.Items.Should().ContainSingle().Which.Sku.Should().Be("PCT-1");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenTextContainsLiteralUnderscore_UnderscoreIsNotTreatedAsWildcard()
    {
        // Per CAT-SEC-001 / CAT-RV-H03: a user-supplied "_" must be a literal, not a single-char wildcard.
        await SeedRowsAsync(
            ProductSearchViewRowBuilder.Active(sku: "UND-1", name: "a_b"),
            ProductSearchViewRowBuilder.Active(sku: "UND-2", name: "aXb"));

        var body = await SearchAsync("?Text=a_b&Page=1&Limit=10");

        body.Items.Should().ContainSingle().Which.Sku.Should().Be("UND-1");
    }

    [Fact]
    public async Task WhenPaging_ReturnsCorrectSliceAndTotal()
    {
        var rows = Enumerable.Range(0, 5)
            .Select(i => ProductSearchViewRowBuilder.Active($"SKU-{i}", amount: i + 1))
            .ToArray();
        await SeedRowsAsync(rows);

        var body = await SearchAsync("?Page=2&Limit=2");

        using (new AssertionScope())
        {
            body.Total.Should().Be(5);
            // Pin the exact slice, not just its size: rows order by PriceAmount (seeded amount = i + 1)
            // then ProductId, so page 2 / size 2 is deterministically SKU-2, SKU-3. A wrong-offset
            // mutation that still returns 2 rows fails here.
            body.Items.Select(i => i.Sku).Should().Equal("SKU-2", "SKU-3");
        }
    }

    private async Task<Guid> SeedCategoryAsync()
    {
        var (_, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        return body.CategoryId;
    }

    private async Task SeedRowsAsync(params ProductSearchViewRow[] rows)
    {
        var seeder = new CatalogReadModelSeeder(DbContext);
        await seeder.SeedRowsAsync(TestContext.Current.CancellationToken, rows);
    }

    private async Task<SearchProductsResponse> SearchAsync(string queryString)
    {
        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/products{queryString}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken))!;
    }
}
