using System.Net;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.API.Endpoints.Products.CreateProduct;
using Catalog.API.Endpoints.Products.GetProductsByIds;
using Catalog.Application.Products.GetProductsByIds;
using Catalog.FunctionalTests.Common;
using FastEndpoints;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class GetProductsByIdsTests : BaseApiTest
{
    public GetProductsByIdsTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenMixOfFoundAndMissing_ReturnsPartialResult()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        var (_, foundProduct) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId));
        var missingId = Guid.CreateVersion7();

        var (response, body) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductsByIdsEndpoint, GetProductsByIdsRequest, GetProductsByIdsResponse>(
                new GetProductsByIdsRequest { Ids = [foundProduct.ProductId, missingId] });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Products.Should().ContainSingle(p => p.ProductId == foundProduct.ProductId);
            body.MissingProductIds.Should().ContainSingle().Which.Should().Be(missingId);
        }
    }

    [Fact]
    public async Task WhenMoreThan100Ids_ReturnsValidationError()
    {
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToList();

        var (response, _) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductsByIdsEndpoint, GetProductsByIdsRequest, ProblemDetails>(
                new GetProductsByIdsRequest { Ids = ids });

        // FluentValidation rejects via the validator behavior — surfaces as 400 from
        // FastEndpoints' validation pipeline.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity);
    }
}
