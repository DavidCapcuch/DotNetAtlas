using System.Net;
using System.Net.Http.Json;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.API.Endpoints.Products.CreateProduct;
using Catalog.Application.Products.SearchProducts;
using Catalog.FunctionalTests.Common;
using FastEndpoints;

namespace Catalog.FunctionalTests.ApiEndpoints.Categories;

[Collection<FunctionalTestCollection>]
public class GetProductsByCategoryTests : BaseApiTest
{
    public GetProductsByCategoryTests(ApiTestFixture app)
        : base(app)
    {
    }

    // GetProductsByCategoryQueryHandler hard-codes a "Status == Active" filter (M3 design),
    // and the ActivateProductCommand isn't wired today. Re-enable once Active products can
    // be created (Active products will then surface in the by-category projection).
    [Fact(Skip = "Blocked on deferred ActivateProductCommand — handler filters to Active-only products.")]
    public async Task WhenCategoryHasProducts_Returns200_WithItems()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Books"));
        var (_, product) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId));

        // Use raw HttpClient — FastEndpoints' GETAsync<,,> serialises null bool? as
        // includeDescendants= which the binder converts to default false; that is the
        // intended behaviour but the empty value also pollutes other Guid? params elsewhere
        // (see GetCategoryTreeTests for the equivalent). Use the explicit URL here.
        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/categories/{cat.CategoryId}/products",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == product.ProductId);
        }
    }

    [Fact(Skip = "Blocked on deferred ActivateProductCommand — handler filters to Active-only products.")]
    public async Task WhenIncludeDescendantsTrue_ReturnsProductsFromChildCategories()
    {
        var (_, electronics) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));
        var (_, laptops) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: electronics.CategoryId));
        var (_, laptop) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(laptops.CategoryId));

        var response = await HttpClientRegistry.ReadClient.GetAsync(
            $"/api/v1/catalog/categories/{electronics.CategoryId}/products?includeDescendants=true",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchProductsResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Items.Should().Contain(i => i.ProductId == laptop.ProductId);
        }
    }
}
