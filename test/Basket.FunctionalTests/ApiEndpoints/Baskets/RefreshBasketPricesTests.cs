using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using Basket.FunctionalTests.Common;
using FastEndpoints;
using FluentResults;
using NSubstitute;
using Platform.SharedKernel.ValueObjects;

namespace Basket.FunctionalTests.ApiEndpoints.Baskets;

[Collection<FunctionalTestCollection>]
public class RefreshBasketPricesTests : BaseApiTest
{
    public RefreshBasketPricesTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenBasketExists_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogSingle(productId, price: 10m);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        // Now make GetMany return the same snapshot at a higher price
        var newSnapshot = ProductSnapshot.Create(
            sku: "SKU",
            name: "Product",
            price: Money.Create(11m, "EUR").Value,
            capturedAtUtc: DateTimeOffset.UtcNow);
        Catalog.GetManyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<(Guid, ProductSnapshot)>>(
            [
                (productId, newSnapshot),
            ]));

        // Act
        var response = await client.PostAsync(
            "/api/v1/basket/refresh-prices",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhenNoBasket_ReturnsNoContent_Idempotent()
    {
        // The M4 handler treats "no basket" as 204 — diverges from use-cases.md § 2.1.4
        // (404). Documented as a doc/code follow-up in the M8 session summary.
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        var response = await client.PostAsync(
            "/api/v1/basket/refresh-prices",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhenCatalogUnavailable_Returns503()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogSingle(productId, price: 10m);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        Catalog.GetManyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(
                BasketErrors.CatalogUnavailable()));

        // Act
        var response = await client.PostAsync(
            "/api/v1/basket/refresh-prices",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private void StubCatalogSingle(Guid productId, decimal price)
    {
        var snapshot = ProductSnapshot.Create(
            sku: "SKU",
            name: "Product",
            price: Money.Create(price, "EUR").Value,
            capturedAtUtc: DateTimeOffset.UtcNow);
        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot));
    }
}
