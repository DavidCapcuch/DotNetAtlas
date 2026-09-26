using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Api.Endpoints.Baskets.GetByUserId;
using Basket.Application.Baskets.GetByUserId;
using Basket.IntegrationTests.Common;
using FastEndpoints;

namespace Basket.IntegrationTests.ApiEndpoints.Baskets;

[Collection<IntegrationTestCollection>]
public class GetBasketTests : BaseIntegrationTest
{
    public GetBasketTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task GetBasket_WhenNoBasketExists_ReturnsEmptyBasket200()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        // Act
        var (response, body) = await client
            .GETAsync<GetBasketEndpoint, GetBasketResponse>();

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Version.Should().Be(0);
            body.Items.Should().BeEmpty();
            body.Total.Should().BeNull();
            body.UserId.Should().Be(userId);
        }
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task GetBasket_WhenBasketHasItems_ReturnsItemsWithSnapshotPrices()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId, BasketTestData.Snapshot(price: 12.34m, currency: "EUR"));

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 3 });

        // Act
        var (response, body) = await client
            .GETAsync<GetBasketEndpoint, GetBasketResponse>();

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Items.Should().ContainSingle();
            var line = body.Items[0];
            line.ProductId.Should().Be(productId);
            line.Quantity.Should().Be(3);
            line.SnapshotPrice.Amount.Should().Be(12.34m);
            line.SnapshotPrice.Currency.Should().Be("EUR");
            body.Total.Should().NotBeNull();
            body.Total!.Amount.Should().Be(12.34m * 3);
        }
    }
}
