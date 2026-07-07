using System.Net;
using System.Net.Http.Json;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.DiscontinueProduct;
using Catalog.Application.Products.SearchProducts;
using Catalog.FunctionalTests.Common;
using FastEndpoints;

namespace Catalog.FunctionalTests.ApiEndpoints.Products.Admin;

/// <summary>
/// Functional coverage for the admin search endpoint added in #172. Asserts:
///   - admins can see Discontinued products without the show-discontinued feature flag,
///   - read-only callers cannot reach the endpoint (403),
///   - unauthenticated callers get 401.
/// </summary>
[Collection<FunctionalTestCollection>]
public class SearchAdminProductsTests : BaseApiTest
{
    private const string AdminUrl = "/api/v1/catalog/admin/products";

    public SearchAdminProductsTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAdminSearches_DiscontinuedProductsAppearWithoutFeatureFlag()
    {
        // Arrange — create + discontinue a product. The default public endpoint would hide
        // this row unless the feature flag is on; the admin endpoint must surface it
        // regardless.
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        var (_, product) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId, name: "Admin-Visible"));
        await HttpClientRegistry.WriteClient
            .POSTAsync<DiscontinueProductEndpoint, DiscontinueProductRequest>(
                new DiscontinueProductRequest { Id = product.ProductId, Reason = "End-of-life" });

        // Act — admin endpoint requires write scope; we don't toggle the feature flag.
        var response = await HttpClientRegistry.WriteClient.GetAsync(
            $"{AdminUrl}?Status=Discontinued",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == product.ProductId);
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenReadScopeCallerHitsAdminEndpoint_Returns403()
    {
        // Read-scope token must not satisfy WritePolicy; ASP.NET surfaces 403.
        var response = await HttpClientRegistry.ReadClient.GetAsync(
            AdminUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenUnauthenticatedCallerHitsAdminEndpoint_Returns401()
    {
        var response = await HttpClientRegistry.NonAuthClient.GetAsync(
            AdminUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
