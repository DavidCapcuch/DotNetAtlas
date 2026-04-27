using System.Net;
using System.Net.Http.Json;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.Application.Categories.GetCategoryTree;
using Catalog.FunctionalTests.Common;
using FastEndpoints;

namespace Catalog.FunctionalTests.ApiEndpoints.Categories;

[Collection<FunctionalTestCollection>]
public class GetCategoryTreeTests : BaseApiTest
{
    public GetCategoryTreeTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenTreeEmpty_Returns200_WithEmptyNodes()
    {
        // Use raw HttpClient.GetFromJsonAsync rather than FastEndpoints' GETAsync<,,> — the
        // latter serialises a Guid? property as RootCategoryId= (empty value), which the
        // FastEndpoints query-binder converts to Guid.Empty rather than null. The handler
        // then short-circuits the response to an empty tree even when categories exist.
        var response = await HttpClientRegistry.ReadClient
            .GetAsync("/api/v1/catalog/categories/tree", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetCategoryTreeResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Nodes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenTreePopulated_Returns200_WithHierarchy()
    {
        var (_, electronics) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Electronics"));
        await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest(name: "Laptops", parentCategoryId: electronics.CategoryId));

        var response = await HttpClientRegistry.ReadClient
            .GetAsync("/api/v1/catalog/categories/tree", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<GetCategoryTreeResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body!.Nodes.Should().HaveCount(2);
            body.Nodes.Should().Contain(n => n.ParentCategoryId == null && n.Depth == 1);
            body.Nodes.Should().Contain(n => n.ParentCategoryId == electronics.CategoryId && n.Depth == 2);
        }
    }
}
