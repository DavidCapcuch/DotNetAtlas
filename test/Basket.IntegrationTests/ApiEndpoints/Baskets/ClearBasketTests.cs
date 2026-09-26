using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.IntegrationTests.Common;
using FastEndpoints;

namespace Basket.IntegrationTests.ApiEndpoints.Baskets;

[Collection<IntegrationTestCollection>]
public class ClearBasketTests : BaseIntegrationTest
{
    public ClearBasketTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Clear_WhenBasketExists_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        // Act
        var response = await client.DeleteAsync(
            "/api/v1/basket/items",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Clear_WhenNoBasket_ReturnsNoContent_Idempotent()
    {
        // The handler treats "no basket" as 204 — diverges from use-cases.md § 2.1.5
        // (404).

        // Arrange
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        // Act
        var response = await client.DeleteAsync(
            "/api/v1/basket/items",
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
