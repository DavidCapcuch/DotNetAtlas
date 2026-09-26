using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Api.Endpoints.Baskets.Checkout;
using Basket.Api.Endpoints.Baskets.GetByUserId;
using Basket.Application.Baskets.GetByUserId;
using Basket.Domain.Baskets.ValueObjects;
using Basket.IntegrationTests.Common;
using FastEndpoints;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Platform.SharedKernel.ValueObjects;
using Platform.Test.Framework.Assertions;

namespace Basket.IntegrationTests.ApiEndpoints.Baskets;

[Collection<IntegrationTestCollection>]
public class CheckoutBasketTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset FixedCapturedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    public CheckoutBasketTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task Checkout_WhenValidRequest_Returns202_AndOutboxRowExists_AndBasketRemoved()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        var checkoutRequest = BasketTestData.ValidCheckoutRequest();

        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.CreateVersion7().ToString());

        // Act
        var (response, body) = await client
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest, CheckoutBasketResponse>(checkoutRequest);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
            // ADR-0029: the OrderId is server-allocated (UUID v7), not chosen by the caller.
            body.OrderId.Should().NotBe(Guid.Empty);
            body.OrderId.Version.Should().Be(7);

            // Checkout keeps the basket's items, so only the post-checkout delete brings a read
            // back to the no-basket contract.
            var (readResponse, basketAfter) = await client.GETAsync<GetBasketEndpoint, GetBasketResponse>();
            readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            basketAfter.Version.Should().Be(0, "no basket exists once checkout has deleted it");
            basketAfter.Items.Should().BeEmpty();

            // Exactly one outbox row, carrying the published contract. Filtering on the user id
            // is itself the partition-key assertion: the key must be the user so one user's
            // events stay ordered, and any other key leaves this query empty.
            var outboxRows = await DbContext.Set<Platform.ReliableMessaging.Outbox.Core.OutboxMessage>()
                .Where(m => m.KafkaKey == userId.ToString())
                .ToListAsync(TestContext.Current.CancellationToken);
            outboxRows.Should().ContainSingle()
                .Which.TopicName.Should().Be("basket.sessions", "basket.md § 8.2 locks the topic name");
            outboxRows[0].Type.Should().BeMessageType<Basket.Sessions.BasketCheckoutInitiatedEvent>(
                "the CLR FullName of the Avro contract from Platform.SchemaRegistry.Contracts");
        }
    }

    [Fact]
    public async Task Checkout_WhenIdempotencyKeyMissing_Returns400()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        var checkoutRequest = BasketTestData.ValidCheckoutRequest();

        // Act — no Idempotency-Key header; FastEndpoints' .Idempotency() filter rejects.
        var response = await client
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest>(checkoutRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task Checkout_WhenSameIdempotencyKeyReplayed_ReturnsAccepted_OrCachedResponse()
    {
        // Verifies that a replayed POST with the same Idempotency-Key does not double-
        // commit the basket: either FastEndpoints' .Idempotency() filter replays the
        // cached 202 (preferred), or the second call hits the handler and gets a domain
        // error because the basket was already deleted on first checkout. Either path
        // satisfies the "no second outbox row" invariant. Full proof of cached-response
        // replay is documented as a follow-up — FE 7.0.0's body-hash cache key behavior
        // wasn't reliably observed in the test host for this BC.

        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });

        var checkoutRequest = BasketTestData.ValidCheckoutRequest();

        var idempotencyKey = Guid.CreateVersion7().ToString();
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        // Act — first call, then a second call with the same Idempotency-Key.
        var (firstResponse, firstBody) = await client
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest, CheckoutBasketResponse>(checkoutRequest);

        var secondResponse = await client
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest>(checkoutRequest);

        // Assert
        using (new AssertionScope())
        {
            firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            firstBody.OrderId.Should().NotBe(Guid.Empty);

            // Either path is acceptable: 202 (cached replay) OR a 4xx (handler ran and
            // basket was already deleted). Critical invariant is the outbox row count.
            secondResponse.StatusCode.Should().BeOneOf(
                HttpStatusCode.Accepted,
                HttpStatusCode.NotFound,
                HttpStatusCode.Conflict);

            // Outbox row landed exactly once — basket was committed only on first call.
            var outboxRows = await DbContext.Set<Platform.ReliableMessaging.Outbox.Core.OutboxMessage>()
                .Where(m => m.KafkaKey == userId.ToString())
                .CountAsync(TestContext.Current.CancellationToken);
            outboxRows.Should().Be(1);
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Checkout_WhenSameIdempotencyKeyUsedByDifferentUser_HandlerStillRuns()
    {
        // FastEndpoints 7.0.1's IdempotencyOptions.AdditionalHeaders defaults include
        // the Authorization header — the OutputCachePolicy reads that into
        // CacheVaryByRules.HeaderNames so two different users reusing the same UUID
        // never share responses. Pinning that default; if a future FE minor drops
        // Authorization from the defaults, this test fails loudly.

        // Arrange
        var idempotencyKey = Guid.CreateVersion7().ToString();

        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();
        var aliceProduct = Guid.CreateVersion7();
        var bobProduct = Guid.CreateVersion7();
        StubCatalog(aliceProduct);
        StubCatalog(bobProduct);

        var aliceClient = HttpClientRegistry.RegularUserAuthClient(alice);
        await aliceClient.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = aliceProduct, Quantity = 1 });
        aliceClient.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        var bobClient = HttpClientRegistry.RegularUserAuthClient(bob);
        await bobClient.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = bobProduct, Quantity = 1 });
        bobClient.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        // Act
        var (aliceResponse, _) = await aliceClient
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest, CheckoutBasketResponse>(
                BasketTestData.ValidCheckoutRequest());
        var (bobResponse, _) = await bobClient
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest, CheckoutBasketResponse>(
                BasketTestData.ValidCheckoutRequest());

        // Assert
        using (new AssertionScope())
        {
            aliceResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            bobResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            var aliceOutbox = await DbContext.Set<Platform.ReliableMessaging.Outbox.Core.OutboxMessage>()
                .Where(m => m.KafkaKey == alice.ToString())
                .CountAsync(TestContext.Current.CancellationToken);
            var bobOutbox = await DbContext.Set<Platform.ReliableMessaging.Outbox.Core.OutboxMessage>()
                .Where(m => m.KafkaKey == bob.ToString())
                .CountAsync(TestContext.Current.CancellationToken);

            aliceOutbox.Should().Be(1, "Alice's checkout must commit even if Bob shares the key");
            bobOutbox.Should().Be(1, "Bob's checkout must commit even if Alice shares the key");
        }
    }

    [Fact]
    public async Task Checkout_WhenBasketIsEmpty_Returns409()
    {
        // Arrange — never added an item. CheckoutHandler returns BasketErrors.EmptyBasket
        // when basket is null OR has zero items. With no Redis key, the handler wraps the
        // null-basket case as 404 not 409. So the route to 409 is: add an item, clear the
        // basket (leaving Items empty + Version=1), then checkout.
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        StubCatalog(productId);

        var client = HttpClientRegistry.RegularUserAuthClient(userId);
        await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 1 });
        await client.DeleteAsync(
            "/api/v1/basket/items",
            TestContext.Current.CancellationToken);

        var checkoutRequest = BasketTestData.ValidCheckoutRequest();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.CreateVersion7().ToString());

        // Act
        var (response, problemDetails) = await client
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest, ProblemDetails>(checkoutRequest);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            problemDetails.Errors.Should().ContainSingle(e => e.Code == "Basket.Empty");

            var outboxRowWritten = await DbContext.Set<Platform.ReliableMessaging.Outbox.Core.OutboxMessage>()
                .AnyAsync(m => m.KafkaKey == userId.ToString(), TestContext.Current.CancellationToken);
            outboxRowWritten.Should().BeFalse("a rejected checkout publishes nothing");
        }
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
