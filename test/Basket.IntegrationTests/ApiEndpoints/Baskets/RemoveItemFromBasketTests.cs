using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Domain.Baskets.ValueObjects;
using Basket.FunctionalTests.Common;
using FastEndpoints;
using FluentResults;
using NSubstitute;
using Platform.SharedKernel.ValueObjects;

namespace Basket.FunctionalTests.ApiEndpoints.Baskets;

[Collection<FunctionalTestCollection>]
public class RemoveItemFromBasketTests : BaseApiTest
{
    private static readonly DateTimeOffset FixedCapturedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    public RemoveItemFromBasketTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task RemoveItem_WhenItemPresent_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        // Act
        var response = await DeleteItemAsync(client, productId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveItem_WhenItemAbsent_ReturnsNoContent_Idempotent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var sittingProduct = Guid.CreateVersion7();
        StubCatalog(sittingProduct);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = sittingProduct, Quantity = 1 });

        // Act
        var response = await DeleteItemAsync(client, productId: Guid.CreateVersion7());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveItem_WhenBasketAbsent_ReturnsNoContent_Idempotent()
    {
        // The handler treats "no basket" as a successful idempotent no-op (204),
        // diverging from use-cases.md § 2.1.2 which prescribes 404.

        // Arrange
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        // Act
        var response = await DeleteItemAsync(client, productId: Guid.CreateVersion7());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static Task<HttpResponseMessage> DeleteItemAsync(HttpClient client, Guid productId)
    {
        // Raw HttpClient.DeleteAsync against the route — FastEndpoints' typed
        // DELETEAsync extension serializes a request body for DELETE requests, which
        // some HTTP backends reject; using the URL-only form avoids the foot-gun.
        return client.DeleteAsync(
            $"/api/v1/basket/items/{productId}",
            TestContext.Current.CancellationToken);
    }

    private void StubCatalog(Guid productId)
    {
        var snapshot = ProductSnapshot.Create(
            sku: "SKU",
            name: "Product",
            price: Money.Create(10m, "EUR").Value,
            capturedAtUtc: FixedCapturedAt);
        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot));
    }
}
