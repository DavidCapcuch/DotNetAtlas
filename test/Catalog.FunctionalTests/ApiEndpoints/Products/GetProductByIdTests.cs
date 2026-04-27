using System.Net;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.API.Endpoints.Products.CreateProduct;
using Catalog.API.Endpoints.Products.GetProductById;
using Catalog.Application.Products.GetProductById;
using Catalog.FunctionalTests.Common;
using FastEndpoints;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class GetProductByIdTests : BaseApiTest
{
    public GetProductByIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenProductExists_Returns200_WithFullDto()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        var createReq = CatalogTestData.ValidCreateProductRequest(cat.CategoryId, name: "Acme Pro");
        var (_, created) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(createReq);

        var (response, body) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductByIdEndpoint, GetProductByIdRequest, GetProductByIdResponse>(
                new GetProductByIdRequest { Id = created.ProductId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.ProductId.Should().Be(created.ProductId);
            body.Name.Should().Be("Acme Pro");
            body.Sku.Should().Be(createReq.Sku);
            body.Price.Currency.Should().Be(createReq.Price.Currency);
            // CreateProduct lands the aggregate in Draft. There is no Activate application
            // command yet (deferred — Domain has Product.Activate but no command exposes it),
            // so newly-created products remain Draft.
            body.Status.Should().Be("Draft");
        }
    }

    [Fact]
    public async Task WhenProductMissing_Returns404()
    {
        var (response, problemDetails) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductByIdEndpoint, GetProductByIdRequest, ProblemDetails>(
                new GetProductByIdRequest { Id = Guid.CreateVersion7() });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Product.NotFound");
        }
    }
}
