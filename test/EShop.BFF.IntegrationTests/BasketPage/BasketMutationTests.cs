using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.BFF.Api.Responses;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.BasketPage;

/// <summary>
/// End-to-end coverage of the basket item-mutation forwarders (bff.md § 3.6, issue #330): each route
/// forwards verbatim to Basket via the <c>basket.write</c> RFC 8693 exchange, relays Basket's status, and on
/// a 2xx <b>synchronously</b> invalidates the buyer's <c>basket-bff-{userId}</c> read cache so the next
/// <c>GET /basket</c> reflects the change. Basket is faked by WireMock; the real per-user FusionCache and
/// token-exchange client run.
/// </summary>
[Collection<BasketPageTestCollection>]
public sealed class BasketMutationTests(BasketPageTestFixture fixture) : BaseBasketPageTest(fixture)
{
    private static readonly Guid ProductA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DateTimeOffset StubTimestamp = new(2026, 06, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task AddItem_WhenBasketReturns204_Returns204AndInvalidatesTheBuyersCache()
    {
        // Arrange — cache an (empty) basket page, then add an item.
        var userId = Guid.NewGuid();
        await PopulateBasketCacheAsync(userId);
        Fixture.StubBasketAddItem(204);

        // Act
        var response = await SendAsync(HttpMethod.Post, "/api/v1/bff/basket/items", userId,
            body: new { productId = ProductA, quantity = 2 });

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await Fixture.IsBasketCachedAsync(userId))
                .Should().BeFalse("a successful mutation synchronously invalidates the buyer's basket cache");
        }
    }

    [Fact]
    public async Task AddItem_WithIdempotencyKeyHeader_ForwardsItToBasketUnchanged()
    {
        // Arrange
        var userId = Guid.NewGuid();
        Fixture.StubBasketAddItem(204);

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bff/basket/items")
        {
            Content = JsonContent.Create(new { productId = ProductA, quantity = 1 }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.CreateUserToken(userId));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "key-42");
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert — the BFF owns no idempotency here; it relays the key for Basket's .Idempotency() (bff.md § 3.6).
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            Fixture.HeaderOnLastRequestTo("/api/v1/basket/items", "Idempotency-Key").Should().Be("key-42");
        }
    }

    [Fact]
    public async Task AddItem_WhenBasketDeclinesWith409_RelaysStatusAndProblemBodyAndDoesNotInvalidate()
    {
        // Arrange — a declined mutation changed no state, so the cache must survive; Basket's problem
        // details (EmptyBasket vs MaxItemsReached carry different UX flows) must reach the caller verbatim.
        const string problemJson = /*lang=json,strict*/
            """{"type":"urn:basket:max-items-reached","title":"Conflict","status":409}""";
        var userId = Guid.NewGuid();
        await PopulateBasketCacheAsync(userId);
        Fixture.StubBasketAddItemProblem(409, problemJson);

        // Act
        var response = await SendAsync(HttpMethod.Post, "/api/v1/bff/basket/items", userId,
            body: new { productId = ProductA, quantity = 2 });

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Be(problemJson, "the forwarder relays Basket's verdict body verbatim (bff.md § 3.6)");
            (await Fixture.IsBasketCachedAsync(userId))
                .Should().BeTrue("a non-2xx verdict changed no basket state, so the cache is not invalidated");
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task AddItem_WhenBasketIsDown_Returns503()
    {
        // Arrange — a 5xx upstream is shielded as a 503, never leaked.
        var userId = Guid.NewGuid();
        Fixture.StubBasketAddItem(500);

        // Act
        var response = await SendAsync(HttpMethod.Post, "/api/v1/bff/basket/items", userId,
            body: new { productId = ProductA, quantity = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddItem_WhenForwardingToBasket_ExchangesTheUserTokenForTheBasketWriteScope()
    {
        // Arrange — only a mutation runs, so the sole token-exchange request is the write client's.
        var userId = Guid.NewGuid();
        Fixture.StubBasketAddItem(204);

        // Act
        var response = await SendAsync(HttpMethod.Post, "/api/v1/bff/basket/items", userId,
            body: new { productId = ProductA, quantity = 1 });

        // Assert — AC #1: the write surface uses the basket.write exchange, not basket.read. (Basket enforces
        // only the audience, not the scope, so without this the scope wiring would be silently unverifiable.)
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            Fixture.LastTokenExchangeScope().Should().Be("basket.write");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddItem_WhenAnonymous_Returns401()
    {
        // Act — no Authorization header.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bff/basket/items")
        {
            Content = JsonContent.Create(new { productId = ProductA, quantity = 1 }),
        };
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task AddItem_WhenSubClaimIsNotAGuid_FailsClosedWith401()
    {
        // Arrange — the token validates (signature + audience) but its sub is not a buyer id: the forwarder
        // must fail closed before any exchange or forward. Were the malformed principal forwarded anyway,
        // Basket's stubbed verdict (not 401) would be relayed and this assertion would catch it.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bff/basket/items")
        {
            Content = JsonContent.Create(new { productId = ProductA, quantity = 1 }),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", Fixture.CreateUserToken("not-a-guid"));

        // Act
        var response = await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangeQuantity_WhenBasketReturns204_Returns204AndInvalidates()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await PopulateBasketCacheAsync(userId);
        Fixture.StubBasketChangeQuantity(ProductA, 204);

        // Act
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/bff/basket/items/{ProductA}/quantity", userId,
            body: new { newQuantity = 5 });

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await Fixture.IsBasketCachedAsync(userId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RemoveItem_WhenBasketReturns204_Returns204AndInvalidates()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await PopulateBasketCacheAsync(userId);
        Fixture.StubBasketRemoveItem(ProductA, 204);

        // Act
        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/bff/basket/items/{ProductA}", userId, body: null);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await Fixture.IsBasketCachedAsync(userId)).Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task GetBasket_ImmediatelyAfterAMutation_ReflectsTheNewBasketState()
    {
        // Arrange — AC #2 as written: not just "the cache entry is gone" but a literal follow-up GET /basket
        // observing the mutated basket. Compose + cache the quantity-2 page first.
        var userId = Guid.NewGuid();
        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 2)]));
        Fixture.StubCatalogByIds(ByIdsBody([Product(ProductA, 10m)]));
        Fixture.StubInventoryBulk(BulkBody([Stock(ProductA, 10)]));

        var before = await GetBasketPageAsync(userId);
        before.Items.Single().Quantity.Should().Be(2, "precondition: the pre-mutation page is cached");

        // The mutation commits in Basket, whose read now returns quantity 5 (newest stub wins).
        Fixture.StubBasket(BasketBody(userId, [Item(ProductA, 10m, 5)]));
        Fixture.StubBasketChangeQuantity(ProductA, 204);

        // Act
        var mutation = await SendAsync(HttpMethod.Put, $"/api/v1/bff/basket/items/{ProductA}/quantity", userId,
            body: new { newQuantity = 5 });
        var after = await GetBasketPageAsync(userId);

        // Assert — without the synchronous invalidation, the cached quantity-2 page would still be served
        // (the TTL has not elapsed within this test).
        using (new AssertionScope())
        {
            mutation.StatusCode.Should().Be(HttpStatusCode.NoContent);
            after.Items.Single().Quantity.Should().Be(5, "the next GET /basket must see the mutation (AC #2)");
        }
    }

    [Fact]
    public async Task Clear_WhenBasketReturns204_Returns204AndInvalidates()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await PopulateBasketCacheAsync(userId);
        Fixture.StubBasketClear(204);

        // Act
        var response = await SendAsync(HttpMethod.Delete, "/api/v1/bff/basket/items", userId, body: null);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await Fixture.IsBasketCachedAsync(userId)).Should().BeFalse();
        }
    }

    /// <summary>Composes + caches the buyer's (empty) basket page so an invalidation is observable.</summary>
    private async Task PopulateBasketCacheAsync(Guid userId)
    {
        Fixture.StubBasketStatus(404); // no basket yet → empty page, cached, not stale
        var get = await SendAsync(HttpMethod.Get, "/api/v1/bff/basket", userId, body: null);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Fixture.IsBasketCachedAsync(userId)).Should().BeTrue("the basket page was just composed and cached");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid userId, object? body)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.CreateUserToken(userId));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<BasketPageResponse> GetBasketPageAsync(Guid userId)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/bff/basket", userId, body: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
