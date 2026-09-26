using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Api.Endpoints.Baskets.ChangeItemQuantity;
using Basket.IntegrationTests.Common;
using FastEndpoints;

namespace Basket.IntegrationTests.ApiEndpoints.Baskets;

[Collection<IntegrationTestCollection>]
public class ChangeItemQuantityTests : BaseIntegrationTest
{
    public ChangeItemQuantityTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task ChangeQuantity_WhenItemPresent_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        var changeRequest = new ChangeItemQuantityRequest { ProductId = productId, NewQuantity = 5 };

        // Act
        var response = await client
            .PUTAsync<ChangeItemQuantityEndpoint, ChangeItemQuantityRequest>(changeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ChangeQuantity_WhenItemNotInBasket_Returns404()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        var unknownProduct = Guid.CreateVersion7();
        var changeRequest = new ChangeItemQuantityRequest { ProductId = unknownProduct, NewQuantity = 2 };

        // Act
        var (response, problemDetails) = await client
            .PUTAsync<ChangeItemQuantityEndpoint, ChangeItemQuantityRequest, ProblemDetails>(changeRequest);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Basket.ItemNotFound");
        }
    }
}
