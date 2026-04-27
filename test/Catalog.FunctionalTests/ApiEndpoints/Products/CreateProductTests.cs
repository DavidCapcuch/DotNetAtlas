using System.Net;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.API.Endpoints.Products.CreateProduct;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class CreateProductTests : BaseApiTest
{
    public CreateProductTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenValidRequest_Returns201_AndOutboxRow_AndProjectionRow()
    {
        var categoryId = await SeedCategoryAsync();
        var request = CatalogTestData.ValidCreateProductRequest(categoryId);

        var (response, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            body.ProductId.Should().NotBeEmpty();

            // The DispatchDomainEventsInterceptor fires both the projection handler AND the
            // outbox publisher in the same SaveChangesAsync — assert both wrote rows.
            var projectionRowExists = await DbContext.ProductSearchView
                .AnyAsync(r => r.ProductId == body.ProductId, TestContext.Current.CancellationToken);
            projectionRowExists.Should().BeTrue("projection handler runs in the same UoW as the write");

            var outboxRows = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == body.ProductId.ToString())
                .CountAsync(TestContext.Current.CancellationToken);
            outboxRows.Should().BeGreaterThanOrEqualTo(1, "ProductCreatedEvent must hit the outbox");
        }
    }

    [Fact]
    public async Task WhenSkuAlreadyExists_Returns409()
    {
        var categoryId = await SeedCategoryAsync();
        var sharedSku = $"SKU-DUPLICATE-{Guid.CreateVersion7():N}".Substring(0, 24);
        var first = CatalogTestData.ValidCreateProductRequest(categoryId, sku: sharedSku);
        var duplicate = CatalogTestData.ValidCreateProductRequest(categoryId, sku: sharedSku);

        await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(first);

        var (response, problemDetails) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, ProblemDetails>(duplicate);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Product.SkuAlreadyExists");
        }
    }

    [Fact]
    public async Task WhenNotAuthenticated_Returns401()
    {
        var categoryId = await SeedCategoryAsync();
        var request = CatalogTestData.ValidCreateProductRequest(categoryId);

        var response = await HttpClientRegistry.NonAuthClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest>(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenAuthenticatedWithReadScopeOnly_Returns403()
    {
        var categoryId = await SeedCategoryAsync();
        var request = CatalogTestData.ValidCreateProductRequest(categoryId);

        var response = await HttpClientRegistry.ReadClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest>(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> SeedCategoryAsync()
    {
        var (response, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return body.CategoryId;
    }
}
