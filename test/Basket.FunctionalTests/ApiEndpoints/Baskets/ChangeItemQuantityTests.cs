using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Api.Endpoints.Baskets.ChangeItemQuantity;
using Basket.Domain.Baskets.ValueObjects;
using Basket.FunctionalTests.Common;
using FastEndpoints;
using FluentResults;
using NSubstitute;
using Platform.SharedKernel.ValueObjects;

namespace Basket.FunctionalTests.ApiEndpoints.Baskets;

[Collection<FunctionalTestCollection>]
public class ChangeItemQuantityTests : BaseApiTest
{
    public ChangeItemQuantityTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenItemPresent_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

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
    public async Task WhenItemNotInBasket_Returns404()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

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

    private void StubCatalog(Guid productId)
    {
        var snapshot = ProductSnapshot.Create(
            sku: "SKU",
            name: "Product",
            price: Money.Create(10m, "EUR").Value,
            capturedAtUtc: DateTimeOffset.UtcNow);
        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot));
    }
}
