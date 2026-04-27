using System.Net;
using Catalog.API.Endpoints.Categories.CreateCategory;
using Catalog.API.Endpoints.Products.CreateProduct;
using Catalog.API.Endpoints.Products.UpdateProductPrice;
using Catalog.Application.Products.CreateProduct;
using Catalog.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;

namespace Catalog.FunctionalTests.ApiEndpoints.Products;

[Collection<FunctionalTestCollection>]
public class UpdateProductPriceTests : BaseApiTest
{
    public UpdateProductPriceTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenValidRequest_Returns204_AndProjectionPriceUpdated_AndOutboxRow()
    {
        var (categoryId, productId) = await SeedCategoryAndProductAsync(originalAmount: 19.99m);

        var request = new UpdateProductPriceRequest
        {
            Id = productId,
            NewPrice = new MoneyDto { Amount = 24.50m, Currency = "EUR" },
        };

        var response = await HttpClientRegistry.WriteClient
            .PUTAsync<UpdateProductPriceEndpoint, UpdateProductPriceRequest>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var projectedPrice = await DbContext.ProductSearchView.AsNoTracking()
                .Where(r => r.ProductId == productId)
                .Select(r => r.PriceAmount)
                .SingleAsync(TestContext.Current.CancellationToken);
            projectedPrice.Should().Be(24.50m);

            var priceChangedRows = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == productId.ToString()
                            && m.Type!.Contains("ProductPriceChanged"))
                .CountAsync(TestContext.Current.CancellationToken);
            priceChangedRows.Should().Be(1);
        }

        _ = categoryId;
    }

    [Fact]
    public async Task WhenProductMissing_Returns404()
    {
        var request = new UpdateProductPriceRequest
        {
            Id = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 1m, Currency = "EUR" },
        };

        var (response, problemDetails) = await HttpClientRegistry.WriteClient
            .PUTAsync<UpdateProductPriceEndpoint, UpdateProductPriceRequest, ProblemDetails>(request);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Product.NotFound");
        }
    }

    private async Task<(Guid CategoryId, Guid ProductId)> SeedCategoryAndProductAsync(decimal originalAmount)
    {
        var (catResp, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());
        catResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var (prodResp, prod) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId, amount: originalAmount));
        prodResp.StatusCode.Should().Be(HttpStatusCode.Created);

        return (cat.CategoryId, prod.ProductId);
    }
}
