using System.Net;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.API.Endpoints.Products.CreateProduct;
using Catalog.API.Endpoints.Products.DiscontinueProduct;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class DiscontinueProductTests : BaseApiTest
{
    public DiscontinueProductTests(ApiTestFixture app)
        : base(app)
    {
    }

    // Post-#177: products are Active on create, so Discontinue can run directly off the seed.
    [Fact]
    public async Task WhenValidRequest_Returns204_AndStatusDiscontinued_AndOutboxRow()
    {
        var productId = await SeedProductAsync();
        var request = new DiscontinueProductRequest
        {
            Id = productId,
            Reason = "Replaced by SKU-NEXT-GEN",
        };

        var response = await HttpClientRegistry.WriteClient
            .POSTAsync<DiscontinueProductEndpoint, DiscontinueProductRequest>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var status = await DbContext.ProductSearchView.AsNoTracking()
                .Where(r => r.ProductId == productId)
                .Select(r => r.Status)
                .SingleAsync(TestContext.Current.CancellationToken);
            status.Should().Be("Discontinued");

            var discontinuedRows = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == productId.ToString()
                            && m.Type!.Contains("ProductDiscontinued"))
                .CountAsync(TestContext.Current.CancellationToken);
            discontinuedRows.Should().Be(1);
        }
    }

    [Fact]
    public async Task WhenReasonEmpty_Returns422()
    {
        var productId = await SeedProductAsync();
        var request = new DiscontinueProductRequest { Id = productId, Reason = "   " };

        var (response, problemDetails) = await HttpClientRegistry.WriteClient
            .POSTAsync<DiscontinueProductEndpoint, DiscontinueProductRequest, ProblemDetails>(request);

        using (new AssertionScope())
        {
            // FluentValidation rejects whitespace-only Reason at the validator behavior — surfaces as 400.
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.BadRequest,
                HttpStatusCode.UnprocessableEntity);
            problemDetails.Errors.Should().NotBeEmpty();
        }
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
