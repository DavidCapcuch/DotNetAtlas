using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Api.Endpoints.Baskets.Clear;
using Basket.Domain.Baskets.ValueObjects;
using Basket.FunctionalTests.Common;
using FastEndpoints;
using FluentResults;
using NSubstitute;
using Platform.SharedKernel.ValueObjects;

namespace Basket.FunctionalTests.ApiEndpoints.Baskets;

[Collection<FunctionalTestCollection>]
public class ClearBasketTests : BaseApiTest
{
    public ClearBasketTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenBasketExists_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

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
    public async Task WhenNoBasket_ReturnsNoContent_Idempotent()
    {
        // The M4 handler treats "no basket" as 204 — diverges from use-cases.md § 2.1.5
        // (404). Documented as a doc/code follow-up in the M8 session summary.
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        var response = await client.DeleteAsync(
            "/api/v1/basket/items",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
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
