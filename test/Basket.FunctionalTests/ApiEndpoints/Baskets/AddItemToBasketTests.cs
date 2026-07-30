using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Application.Baskets.Common.Errors;
using Basket.Domain.Baskets.ValueObjects;
using Basket.FunctionalTests.Common;
using FastEndpoints;
using FluentResults;
using NSubstitute;
using Platform.SharedKernel.ValueObjects;

namespace Basket.FunctionalTests.ApiEndpoints.Baskets;

[Collection<FunctionalTestCollection>]
public class AddItemToBasketTests : BaseApiTest
{
    private static readonly DateTimeOffset FixedCapturedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    public AddItemToBasketTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task AddItem_WhenValidRequest_ReturnsNoContent_AndItemReadable()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        var request = new AddItemToBasketRequest { ProductId = productId, Quantity = 2 };

        // Act
        var response = await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(request);

        // Assert — verify via the API rather than Redis key naming. FusionCache
        // wraps payloads with its own envelope so a literal "basket:{userId}" lookup
        // is brittle; GET round-trips through the same cache and is the contract
        // callers actually use.
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var (getResponse, body) = await client
                .GETAsync<Basket.Api.Endpoints.Baskets.GetByUserId.GetBasketEndpoint, Basket.Application.Baskets.GetByUserId.GetBasketResponse>();
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Items.Should().ContainSingle(i => i.ProductId == productId && i.Quantity == 2);
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddItem_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var request = new AddItemToBasketRequest { ProductId = Guid.CreateVersion7(), Quantity = 1 };

        // Act
        var response = await HttpClientRegistry.NonAuthClient
            .POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddItem_WhenCatalogReturnsProductNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<ProductSnapshot>(BasketAclErrors.ProductNotFound(productId)));

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        var request = new AddItemToBasketRequest { ProductId = productId, Quantity = 1 };

        // Act
        var (response, problemDetails) = await client
            .POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest, ProblemDetails>(request);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Basket.ProductNotFound");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task AddItem_WhenCatalogUnavailable_Returns503_AndPersistsNoLine()
    {
        // Arrange — this pins the endpoint's half of the contract only: CatalogUnavailable maps to
        // 503 and the failed add writes nothing. It stubs the port, so no binding runs here; which
        // upstream shapes produce CatalogUnavailable is settled in ProductCatalogHttpAdapterTests.
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable()));

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        var request = new AddItemToBasketRequest { ProductId = productId, Quantity = 1 };

        // Act
        var (response, problemDetails) = await client
            .POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest, ProblemDetails>(request);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Basket.CatalogUnavailable");

            var (_, basket) = await client
                .GETAsync<Basket.Api.Endpoints.Baskets.GetByUserId.GetBasketEndpoint, Basket.Application.Baskets.GetByUserId.GetBasketResponse>();
            basket.Items.Should().BeEmpty("a failed add must persist no line");
        }
    }

    [Fact]
    public async Task AddItem_WhenIdempotencyKeyMissing_StillSucceeds_DoubleClickGuardOnly()
    {
        // basket.md § ADR-0013 makes Idempotency-Key OPTIONAL on /items (double-click
        // guard semantics) — REQUIRED only on /checkout. FastEndpoints 7.0.1's bare
        // .Idempotency() decoration on this endpoint does NOT 400 on missing header in
        // this BC's wiring; it only enables response caching when the header is sent.
        // Pinning that observation so a future FE minor that flips the default fails
        // loudly here instead of silently breaking double-click semantics.

        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalogProduct(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        var request = new AddItemToBasketRequest { ProductId = productId, Quantity = 1 };

        // Act
        var response = await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task AddItem_WhenQuantityIsZero_ReturnsValidationError()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        var request = new AddItemToBasketRequest { ProductId = Guid.CreateVersion7(), Quantity = 0 };

        // Act
        var (response, _) = await client
            .POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest, ProblemDetails>(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private void StubCatalogProduct(Guid productId, decimal price = 9.99m, string currency = "EUR")
    {
        var snapshot = ProductSnapshot.Create(
            sku: $"SKU-{productId:N}".Substring(0, 12),
            name: $"Widget-{productId:N}".Substring(0, 12),
            price: Money.Create(price, currency).Value,
            capturedAtUtc: FixedCapturedAt);

        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot));
    }
}
