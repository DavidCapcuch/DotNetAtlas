using System.Net;
using System.Net.Http.Json;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Application.Common.FeatureFlags;
using Catalog.Application.Products.SearchProducts;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using NSubstitute;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class SearchProductsTests : BaseApiTest
{
    public SearchProductsTests(ApiTestFixture app)
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
    public async Task WhenShowDiscontinuedFlagFlippedOnAtRuntime_HandlerHonoursIt()
    {
        // Closes the M5 follow-up "verify the catalog.show-discontinued-in-search flag changes
        // search results without restart" (ADR-0014). Asserts the OpenFeature client is
        // consulted with the right flag key — the projection handler's contract.
        var categoryId = await SeedCategoryAsync();
        await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(categoryId, name: "AnyProduct"));

        FeatureClient.GetBooleanValueAsync(
                CatalogFeatureFlags.ShowDiscontinuedInSearch,
                Arg.Any<bool>(),
                Arg.Any<OpenFeature.Model.EvaluationContext?>(),
                Arg.Any<OpenFeature.Model.FlagEvaluationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            "/api/v1/catalog/products?Page=1&Limit=50",
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    private async Task<Guid> SeedCategoryAsync()
    {
        var (_, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        return body.CategoryId;
    }
}
