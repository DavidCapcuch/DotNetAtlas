using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.BFF.Api.Responses;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.BasketPage;

/// <summary>
/// End-to-end basket-page composition over the real typed clients — the <b>RFC 8693 token-exchange</b>
/// Basket client and the <c>client_credentials</c> Catalog / Inventory clients — and the real per-user
/// FusionCache, with the upstreams faked by WireMock (issue #329 acceptance: enriched compose with
/// drift / out-of-stock flags, empty basket, Catalog / Inventory partial degradation, required auth, and
/// per-user caching).
/// </summary>
[Collection<BasketPageTestCollection>]
public sealed class GetBasketTests(BasketPageTestFixture fixture) : BaseBasketPageTest(fixture)
{
    private static readonly DateTimeOffset StubTimestamp = new(2026, 06, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProductA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GetBasket_WhenComposed_Returns200WithPriceDriftAndOutOfStockFlags()
    {
        // Arrange — A: snapshot 10 ×2, current 12 (drift), available 10; B: snapshot 5 ×3, current 5, available 1 (OOS).
        var userId = Guid.NewGuid();
        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2), Item(ProductB, 5m, 3)]));
        Fixture.StubCatalogByIds(ByIdsBody([Product(ProductA, 12m), Product(ProductB, 5m)]));
        Fixture.StubInventoryBulk(BulkBody([Stock(ProductA, 10), Stock(ProductB, 1)]));

        // Act
        var page = await GetBasketOkAsync(userId);

        // Assert
        using var _ = new AssertionScope();
        page.UserId.Should().Be(userId);
        page.Items.Should().HaveCount(2);

        var itemA = page.Items.Single(item => item.ProductId == ProductA);
        itemA.CurrentPrice!.Amount.Should().Be(12m);
        itemA.PriceDrifted.Should().BeTrue();
        itemA.AvailableQty.Should().Be(10);
        itemA.OutOfStock.Should().BeFalse();

        var itemB = page.Items.Single(item => item.ProductId == ProductB);
        itemB.PriceDrifted.Should().BeFalse();
        itemB.OutOfStock.Should().BeTrue();

        page.TotalSnapshot.Amount.Should().Be(35m);   // 20 + 15
        page.TotalCurrent.Amount.Should().Be(39m);     // 24 + 15
        page.HasPriceDrift.Should().BeTrue();
        page.HasOutOfStock.Should().BeTrue();
        page.HasStaleData.Should().BeFalse();
    }

    [Fact]
    public async Task GetBasket_WhenBasketNotFound_Returns200EmptyNotStale()
    {
        // Arrange — no basket yet (lazily created).
        var userId = Guid.NewGuid();
        Fixture.StubBasketStatus(404);

        // Act
        var response = await SendAsync(userId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadAsync(response);

        using var _ = new AssertionScope();
        page.Items.Should().BeEmpty();
        page.TotalSnapshot.Amount.Should().Be(0m);
        page.TotalCurrent.Amount.Should().Be(0m);
        page.HasStaleData.Should().BeFalse();
        response.Headers.Contains("X-BFF-Stale").Should().BeFalse();
        response.Headers.Contains("X-BFF-PartialData").Should().BeFalse();
    }

    [Fact]
    public async Task GetBasket_WhenCatalogBatchDown_Returns200PartialCatalogWithStale()
    {
        // Arrange
        var userId = Guid.NewGuid();
        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2)]));
        Fixture.StubCatalogByIdsStatus(500);
        Fixture.StubInventoryBulk(BulkBody([Stock(ProductA, 10)]));

        // Act
        var response = await SendAsync(userId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadAsync(response);

        using var _ = new AssertionScope();
        var item = page.Items.Should().ContainSingle().Subject;
        item.CurrentPrice.Should().BeNull();
        item.PriceDrifted.Should().BeFalse();
        item.LineTotalCurrent.Amount.Should().Be(20m, "current falls back to snapshot when Catalog is down");
        page.HasStaleData.Should().BeTrue();
        response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("catalog");
        response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task GetBasket_WhenInventoryBatchDown_Returns200PartialInventoryWithStale()
    {
        // Arrange
        var userId = Guid.NewGuid();
        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2)]));
        Fixture.StubCatalogByIds(ByIdsBody([Product(ProductA, 10m)]));
        Fixture.StubInventoryBulkStatus(500);

        // Act
        var response = await SendAsync(userId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadAsync(response);

        using var _ = new AssertionScope();
        var item = page.Items.Should().ContainSingle().Subject;
        item.AvailableQty.Should().BeNull();
        item.OutOfStock.Should().BeFalse();
        page.HasStaleData.Should().BeTrue();
        response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("inventory");
    }

    [Fact]
    public async Task GetBasket_WhenAnonymous_Returns401()
    {
        // Act — no Authorization header.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/bff/basket");
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBasket_WhenBasketIsDownAndNoCache_Returns503()
    {
        // Arrange — Basket is the gating call.
        var userId = Guid.NewGuid();
        Fixture.StubBasketStatus(500);

        // Act
        var response = await SendAsync(userId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetBasket_WhenCalledTwice_ServesSecondFromPerUserCache()
    {
        // Arrange — first call composes and caches the per-user basket.
        var userId = Guid.NewGuid();
        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2)]));
        Fixture.StubCatalogByIds(ByIdsBody([Product(ProductA, 10m)]));
        Fixture.StubInventoryBulk(BulkBody([Stock(ProductA, 10)]));
        var first = await GetBasketOkAsync(userId);

        // Take Basket down — a non-cached read would now 503.
        Fixture.ResetUpstreams();
        Fixture.StubBasketStatus(500);

        // Act — second call within the 15s TTL is served from the per-user cache, not re-composed.
        var response = await SendAsync(userId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await ReadAsync(response);

        using var _ = new AssertionScope();
        second.Items.Should().ContainSingle().Which.ProductId.Should().Be(ProductA);
        second.HasStaleData.Should().BeFalse("a fresh-within-TTL cache hit is not a fail-safe serve");
        first.Items.Should().ContainSingle();
    }

    private async Task<BasketPageResponse> GetBasketOkAsync(Guid userId)
    {
        var response = await SendAsync(userId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsync(response);
    }

    private async Task<HttpResponseMessage> SendAsync(Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/bff/basket");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.CreateUserToken(userId));
        return await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<BasketPageResponse> ReadAsync(HttpResponseMessage response)
    {
        var page = await response.Content.ReadFromJsonAsync<BasketPageResponse>(TestContext.Current.CancellationToken);
        page.Should().NotBeNull();
        return page!;
    }

    private static object BasketBody(Guid userId, object[] items) => new
    {
        userId,
        version = 4,
        items,
        total = new { amount = 0m, currency = "USD" },
        createdAtUtc = StubTimestamp,
        lastModifiedAtUtc = StubTimestamp,
    };

    private static object Item(Guid productId, decimal snapshot, int quantity) => new
    {
        productId,
        sku = "SKU-" + productId.ToString()[..4],
        name = "Product " + productId.ToString()[..4],
        snapshotPrice = new { amount = snapshot, currency = "USD" },
        quantity,
        capturedAtUtc = StubTimestamp,
        lineTotal = new { amount = snapshot * quantity, currency = "USD" },
    };

    private static object ByIdsBody(object[] products) => new { products, missingProductIds = Array.Empty<Guid>() };

    private static object Product(Guid productId, decimal current) => new
    {
        productId,
        sku = "SKU-" + productId.ToString()[..4],
        name = "Product " + productId.ToString()[..4],
        description = "desc",
        brandName = "Acme",
        categoryPath = "/c",
        categoryBreadcrumb = "C",
        price = new { amount = current, currency = "USD" },
        status = "Active",
        dimensions = (object?)null,
        images = new[] { new { url = "https://cdn/img.jpg", altText = "img", displayOrder = 0 } },
    };

    private static object BulkBody(object[] items) => new { items, missingProductIds = Array.Empty<Guid>() };

    private static object Stock(Guid productId, int available) => new
    {
        productId,
        onHand = available + 2,
        reserved = 2,
        available,
        lastUpdatedUtc = StubTimestamp,
    };
}
