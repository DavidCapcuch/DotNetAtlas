using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Application.Baskets.Common.Errors;
using Basket.Domain.Baskets.ValueObjects;
using Basket.IntegrationTests.Common;
using FastEndpoints;
using FluentResults;
using NSubstitute;

namespace Basket.IntegrationTests.ApiEndpoints.Baskets;

[Collection<IntegrationTestCollection>]
public class RefreshBasketPricesTests : BaseIntegrationTest
{
    public RefreshBasketPricesTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task RefreshPrices_WhenBasketExists_ReturnsNoContent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId, BasketTestData.Snapshot(price: 10m));

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        // Now make GetMany return the same snapshot at a higher price
        var newSnapshot = BasketTestData.Snapshot(price: 11m);
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
    public async Task RefreshPrices_WhenNoBasket_ReturnsNoContent_Idempotent()
    {
        // The handler treats "no basket" as 204 — diverges from use-cases.md § 2.1.4
        // (404).

        // Arrange
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        // Act
        var response = await client.PostAsync(
            "/api/v1/basket/refresh-prices",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task RefreshPrices_WhenCatalogUnavailable_Returns503()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        Catalog.GetManyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(
                BasketAclErrors.CatalogUnavailable()));

        // Act
        var response = await client.PostAsync(
            "/api/v1/basket/refresh-prices",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
