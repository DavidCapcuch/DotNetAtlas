using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Domain.Products.ValueObjects;
using Catalog.IntegrationTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.Test.Framework.Assertions;

namespace Catalog.IntegrationTests.ApiEndpoints.Products;

[Collection<IntegrationTestCollection>]
public class CreateProductTests : BaseIntegrationTest
{
    public CreateProductTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenValidRequest_Returns201_AndOutboxRow_AndProjectionRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var categoryId = await SeedCategoryAsync();
        var request = CatalogTestData.ValidCreateProductRequest(categoryId);

        var (response, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(request);

        // Folded from CreateProductPipelineIntegrationTests (catalog.md § 9): a single SaveChangesAsync
        // commits the write-model aggregate, the product_search_view projection, AND the outbox row
        // atomically. Assert all three through the HTTP entrance so the EF mappings round-trip
        // end-to-end (OwnsOne VOs, owned image collection, Money + currency converter).
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            body.ProductId.Should().NotBeEmpty();

            // Write-model — every OwnsOne VO round-trips through Postgres.
            var product = await DbContext.Products.AsNoTracking()
                .FirstAsync(p => p.Id == body.ProductId, ct);
            product.Sku.Value.Should().Be(request.Sku);
            product.Name.Value.Should().Be(request.Name);
            product.Description.Value.Should().Be(request.Description);
            product.Brand.Value.Should().Be(request.Brand);
            product.Price.Amount.Should().Be(request.Price.Amount);
            product.Price.Currency.Name.Should().Be(request.Price.Currency);
            product.Status.Should().Be(ProductStatus.Active);
            product.Dimensions.Should().NotBeNull();
            product.Images.Should().HaveCount(1);

            // Projection — populated by the in-process domain-event handler in the same UoW as the write.
            var projection = await DbContext.ProductSearchView.AsNoTracking()
                .FirstAsync(r => r.ProductId == body.ProductId, ct);
            projection.Sku.Should().Be(request.Sku);
            projection.CategoryId.Should().Be(categoryId);
            projection.PriceAmount.Should().Be(request.Price.Amount);
            projection.PriceCurrency.Should().Be(request.Price.Currency);
            projection.Status.Should().Be(ProductStatus.Active.Name);
            projection.IsSellable.Should().BeTrue("post-#177 products are Active on create and therefore sellable");

            // The dimensions VO is flattened across four columns on the way in. Values are distinct
            // per axis, so a transposed assignment in the projection handler fails here.
            projection.DimensionsLength.Should().Be(request.Dimensions!.Length);
            projection.DimensionsWidth.Should().Be(request.Dimensions.Width);
            projection.DimensionsHeight.Should().Be(request.Dimensions.Height);
            projection.DimensionsUnit.Should().Be(request.Dimensions.Unit);

            // Outbox — exactly one row, on the products topic, carrying the Avro CLR type name.
            var outboxRows = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == body.ProductId.ToString())
                .ToListAsync(ct);
            outboxRows.Should().ContainSingle()
                .Which.TopicName.Should().Be("catalog.products");
            outboxRows[0].Type.Should().BeMessageType<Catalog.Products.ProductCreatedEvent>();
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
