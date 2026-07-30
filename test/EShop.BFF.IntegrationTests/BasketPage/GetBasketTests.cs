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
    [Trait("Category", "critical-path")]
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
        using (new AssertionScope())
        {
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

        using (new AssertionScope())
        {
            page.Items.Should().BeEmpty();
            page.TotalSnapshot.Amount.Should().Be(0m);
            page.TotalCurrent.Amount.Should().Be(0m);
            page.HasStaleData.Should().BeFalse();
            response.Headers.Contains("X-BFF-Stale").Should().BeFalse();
            response.Headers.Contains("X-BFF-PartialData").Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
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

        using (new AssertionScope())
        {
            var item = page.Items.Should().ContainSingle().Subject;
            item.CurrentPrice.Should().BeNull();
            item.PriceDrifted.Should().BeFalse();
            item.LineTotalCurrent.Amount.Should().Be(20m, "current falls back to snapshot when Catalog is down");
            page.HasStaleData.Should().BeTrue();
            response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("catalog");
            response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
        }
    }

    /// <summary>The two ways a bound member can fail to arrive; each is closed by a different strict-binding
    /// setting on <c>UpstreamJson.Web</c>, so both are exercised.</summary>
    public enum PriceShape
    {
        Omitted,
        Null,
    }

    [Theory]
    [Trait("Category", "resilience")]
    [InlineData(PriceShape.Omitted)]
    [InlineData(PriceShape.Null)]
    public async Task GetBasket_WhenCatalogBatchCannotSupplyPrice_Returns200PartialCatalogWithStale(
        PriceShape priceShape)
    {
        // Arrange — Catalog answers 200, but the item carries no bindable price: either the member is gone
        // from the by-ids contract, or it arrives null. Both must land in the same degradation an
        // unavailable Catalog produces (bff.md § 3.2), never a page composed from a half-bound product.
        var userId = Guid.NewGuid();
        var product = Product(ProductA, current: 12m);
        if (priceShape == PriceShape.Omitted)
        {
            product.Remove("price");
        }
        else
        {
            product["price"] = null;
        }

        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2)]));
        Fixture.StubCatalogByIds(ByIdsBody([product]));
        Fixture.StubInventoryBulk(BulkBody([Stock(ProductA, 10)]));

        // Act
        var response = await SendAsync(userId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadAsync(response);

        using (new AssertionScope())
        {
            var item = page.Items.Should().ContainSingle().Subject;
            item.CurrentPrice.Should().BeNull();
            item.PriceDrifted.Should().BeFalse();
            item.LineTotalCurrent.Amount.Should().Be(20m, "current falls back to snapshot when the batch cannot bind");
            item.AvailableQty.Should().Be(10, "Inventory answered, so only the Catalog half degrades");
            page.HasStaleData.Should().BeTrue();
            response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("catalog");
            response.Headers.GetValues("X-BFF-Stale").Should().ContainSingle().Which.Should().Be("true");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
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

        using (new AssertionScope())
        {
            var item = page.Items.Should().ContainSingle().Subject;
            item.AvailableQty.Should().BeNull();
            item.OutOfStock.Should().BeFalse();
            page.HasStaleData.Should().BeTrue();
            response.Headers.GetValues("X-BFF-PartialData").Should().ContainSingle().Which.Should().Be("inventory");
        }
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task GetBasket_WhenCatalogBatchCarriesOnlyWhatThisPageRenders_ComposesTheFullPage()
    {
        // Arrange — the by-ids item narrowed to the members this page reads, and nothing else: no sku,
        // name, description, status or image alt text. Catalog is free to narrow its batch contract to
        // this, and enrichment has to be unaffected — the BFF binds only what it renders.
        var userId = Guid.NewGuid();
        var narrowed = new Dictionary<string, object?>
        {
            ["productId"] = ProductA,
            ["price"] = new { amount = 12m, currency = "USD" },
            ["images"] = new[] { new { url = "https://cdn/narrow.jpg", displayOrder = 0 } },
        };

        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2)]));
        Fixture.StubCatalogByIds(ByIdsBody([narrowed]));
        Fixture.StubInventoryBulk(BulkBody([Stock(ProductA, 10)]));

        // Act
        var page = await GetBasketOkAsync(userId);

        // Assert — fully enriched, exactly as the wide payload composes.
        using (new AssertionScope())
        {
            var item = page.Items.Should().ContainSingle().Subject;
            item.CurrentPrice!.Amount.Should().Be(12m);
            item.PriceDrifted.Should().BeTrue();
            item.PrimaryImageUrl.Should().Be("https://cdn/narrow.jpg");
            item.LineTotalCurrent.Amount.Should().Be(24m);
            item.AvailableQty.Should().Be(10);
            page.HasStaleData.Should().BeFalse("the narrowed item still carries everything this page renders");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task GetBasket_WhenAnonymous_Returns401()
    {
        // Act — no Authorization header.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/bff/basket");
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "resilience")]
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

        using (new AssertionScope())
        {
            second.Items.Should().ContainSingle().Which.ProductId.Should().Be(ProductA);
            second.HasStaleData.Should().BeFalse("a fresh-within-TTL cache hit is not a fail-safe serve");
            first.Items.Should().ContainSingle();
        }
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

    /// <summary>
    /// A by-ids item as Catalog emits it — every member, including the ones the BFF's ACL record does not
    /// bind. Keyed rather than anonymous so a test can drop or null one member to model a contract change.
    /// </summary>
    private static Dictionary<string, object?> Product(Guid productId, decimal current) => new()
    {
        ["productId"] = productId,
        ["sku"] = "SKU-" + productId.ToString()[..4],
        ["name"] = "Product " + productId.ToString()[..4],
        ["description"] = "desc",
        ["brandName"] = "Acme",
        ["categoryPath"] = "/c",
        ["categoryBreadcrumb"] = "C",
        ["price"] = new { amount = current, currency = "USD" },
        ["status"] = "Active",
        ["dimensions"] = null,
        ["images"] = new[] { new { url = "https://cdn/img.jpg", altText = "img", displayOrder = 0 } },
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
