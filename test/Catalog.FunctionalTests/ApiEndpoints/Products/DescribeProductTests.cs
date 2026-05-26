using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.DescribeProduct;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class DescribeProductTests : BaseApiTest
{
    public DescribeProductTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenValidRequest_Returns204_AndDescriptionUpdated()
    {
        var productId = await SeedProductAsync();

        var request = new DescribeProductRequest
        {
            Id = productId,
            NewDescription = "Updated description",
        };

        var response = await HttpClientRegistry.WriteClient
            .PUTAsync<DescribeProductEndpoint, DescribeProductRequest>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var updatedDescription = await DbContext.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.Description.Value)
                .SingleAsync(TestContext.Current.CancellationToken);
            updatedDescription.Should().Be("Updated description");
        }
    }

    [Fact]
    public async Task WhenProductMissing_Returns404()
    {
        var request = new DescribeProductRequest
        {
            Id = Guid.CreateVersion7(),
            NewDescription = "Anything",
        };

        var (response, _) = await HttpClientRegistry.WriteClient
            .PUTAsync<DescribeProductEndpoint, DescribeProductRequest, ProblemDetails>(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> SeedProductAsync()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        var (_, prod) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId));
        return prod.ProductId;
    }
}
