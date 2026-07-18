using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.GetProductById;
using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.GetProductById;
using Catalog.IntegrationTests.Common;
using FastEndpoints;

namespace Catalog.IntegrationTests.ApiEndpoints.Products;

[Collection<IntegrationTestCollection>]
public class GetProductByIdTests : BaseIntegrationTest
{
    public GetProductByIdTests(IntegrationTestFixture app)
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
            // Post-#177: CreateProduct lands the aggregate directly in Active (Draft removed
            // from the Catalog lifecycle — the only transition is Active ↔ Discontinued).
            body.Status.Should().Be("Active");
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

    [Fact]
    public async Task WhenProductHasImages_Returns200_WithImageAndPriceProjectionMapped()
    {
        // Folded from GetProductByIdQueryHandlerTests: the read model persists images as JSON and
        // price as a decimal column, so this pins the query's image deserialization + MoneyDto
        // amount mapping — observables the create-driven test above does not assert. Seed the
        // projection row directly so the image payload is the thing under test, not the create pipeline.
        var ct = TestContext.Current.CancellationToken;
        var seeder = new CatalogReadModelSeeder(DbContext);
        var row = ProductSearchViewRowBuilder.Active(amount: 42.50m)
            .WithImages(new ImageReferenceDto { Url = "https://cdn.example.com/a.jpg", AltText = "a", DisplayOrder = 0 });
        await seeder.SeedRowsAsync(ct, row);

        var (response, body) = await HttpClientRegistry.ReadClient
            .GETAsync<GetProductByIdEndpoint, GetProductByIdRequest, GetProductByIdResponse>(
                new GetProductByIdRequest { Id = row.ProductId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.ProductId.Should().Be(row.ProductId);
            body.Price.Amount.Should().Be(42.50m);
            body.Images.Should().ContainSingle().Which.AltText.Should().Be("a");
        }
    }
}
