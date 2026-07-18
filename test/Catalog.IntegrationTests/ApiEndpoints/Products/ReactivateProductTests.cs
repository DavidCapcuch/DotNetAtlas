using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Api.Endpoints.Products.DiscontinueProduct;
using Catalog.Api.Endpoints.Products.ReactivateProduct;
using Catalog.IntegrationTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Catalog.IntegrationTests.ApiEndpoints.Products;

[Collection<IntegrationTestCollection>]
public class ReactivateProductTests : BaseIntegrationTest
{
    public ReactivateProductTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    // Both Reactivate scenarios need a Discontinued product to act on. Post-#177 products are
    // Active on create, so Discontinue (Active → Discontinued) sets up the precondition directly.
    [Fact]
    public async Task WhenAdminFlagTrue_Returns204_AndStatusActive()
    {
        var productId = await SeedDiscontinuedProductAsync();

        var request = new ReactivateProductRequest { Id = productId, AdminReactivation = true };

        var response = await HttpClientRegistry.WriteClient
            .POSTAsync<ReactivateProductEndpoint, ReactivateProductRequest>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var status = await DbContext.ProductSearchView.AsNoTracking()
                .Where(r => r.ProductId == productId)
                .Select(r => r.Status)
                .SingleAsync(TestContext.Current.CancellationToken);
            status.Should().Be("Active");
        }
    }

    [Fact]
    public async Task WhenAdminFlagFalse_Returns403()
    {
        var productId = await SeedDiscontinuedProductAsync();

        var request = new ReactivateProductRequest { Id = productId, AdminReactivation = false };

        var (response, problemDetails) = await HttpClientRegistry.WriteClient
            .POSTAsync<ReactivateProductEndpoint, ReactivateProductRequest, ProblemDetails>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Product.ReactivationRequiresAdminFlag");
        }
    }

    private async Task<Guid> SeedDiscontinuedProductAsync()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        var (_, prod) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId));

        await HttpClientRegistry.WriteClient
            .POSTAsync<DiscontinueProductEndpoint, DiscontinueProductRequest>(
                new DiscontinueProductRequest { Id = prod.ProductId, Reason = "End-of-life" });

        return prod.ProductId;
    }
}
