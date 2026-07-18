using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.GetProductsByIds;
using Catalog.Application.Products.GetProductsByIds;
using Catalog.IntegrationTests.Common;
using FastEndpoints;

namespace Catalog.IntegrationTests.ApiEndpoints.Products;

[Collection<IntegrationTestCollection>]
public class GetProductsByIdsTests : BaseIntegrationTest
{
    public GetProductsByIdsTests(IntegrationTestFixture app)
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
    public async Task WhenAllIdsUnknown_ReturnsEmptyProductsAndAllMissing()
    {
        // Folded from GetProductsByIdsQueryHandlerTests: a distinct branch from the mixed case above —
        // every requested id is absent, so Products is empty and every id surfaces as missing.
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var (response, body) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductsByIdsEndpoint, GetProductsByIdsRequest, GetProductsByIdsResponse>(
                new GetProductsByIdsRequest { Ids = [a, b] });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Products.Should().BeEmpty();
            body.MissingProductIds.Should().BeEquivalentTo([a, b]);
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
