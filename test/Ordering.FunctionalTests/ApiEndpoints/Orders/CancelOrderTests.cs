using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.API.Endpoints.Orders.CancelOrder;
using Ordering.Domain.Orders;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;
using Ordering.Orders;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework.Kafka;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection<FunctionalTestCollection>]
public class CancelOrderTests : BaseApiTest
{
    public CancelOrderTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenIdempotencyKeyHeaderMissing_ReturnsBadRequest()
    {
        // ADR-0013 § Header contract: a protected endpoint without the
        // Idempotency-Key header surfaces as 400 from FastEndpoints'
        // IdempotencyPolicy before the request even reaches auth.
        var response = await HttpClientRegistry.BuyerClient
            .PostAsJsonAsync(
                $"/api/v1/ordering/orders/{Guid.CreateVersion7()}/cancel",
                new CancelOrderRequest { OrderId = Guid.CreateVersion7(), Reason = "x" },
                TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Sending the Idempotency-Key header so the policy lets the request
        // through to authentication; otherwise 400 from the missing-header
        // branch would mask the auth check.
        var response = await PostCancelAsync(
            HttpClientRegistry.NonAuthClient,
            Guid.CreateVersion7(),
            "x");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenReasonEmpty_ReturnsClientError()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var response = await PostCancelAsync(
            HttpClientRegistry.BuyerClient,
            order.Id,
            string.Empty);

        // FastEndpoints' validation pipeline + AddProblemDetails maps
        // FluentValidation failures to 400 by default; either 400 or 422
        // is acceptable per ordering.md § 9.2.
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    [Fact]
    public async Task WhenBuyerCancelsOwnCreatedOrder_ReturnsNoContent()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var response = await PostCancelAsync(
            HttpClientRegistry.BuyerClient,
            order.Id,
            "changed mind");

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var refreshed = await DbContext.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(OrderStatus.Cancelled);
        }
    }

    [Fact]
    public async Task WhenAnotherBuyerTriesToCancel_ReturnsNotFound()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var response = await PostCancelAsync(
            HttpClientRegistry.OtherBuyerClient,
            order.Id,
            "trying");

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Existence must NOT be leaked — order is still in Created.
            var refreshed = await DbContext.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(OrderStatus.Created);
        }
    }

    [Fact]
    public async Task WhenOrderShipped_ReturnsConflict()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateShippedOrderAsync(TestUsers.AdminId);

        var response = await PostCancelAsync(
            HttpClientRegistry.AdminClient,
            order.Id,
            "too late");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WhenSameIdempotencyKeyReplayed_HandlerInvokedOnceOnly()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        // The fixture replaces IOutboxWriter with FakeOutboxWriter so we can
        // count handler invocations via captured Avro messages without a
        // real schema registry. Reset captures from the seed-time
        // OrderCreatedEvent so the assertion below counts cancellations only.
        var fakeOutbox = (FakeOutboxWriter)App.Services.GetRequiredService<IOutboxWriter>();
        fakeOutbox.Clear();

        var idempotencyKey = Guid.NewGuid().ToString();

        var first = await PostCancelAsync(
            HttpClientRegistry.BuyerClient,
            order.Id,
            "double-click",
            idempotencyKey);

        var second = await PostCancelAsync(
            HttpClientRegistry.BuyerClient,
            order.Id,
            "double-click",
            idempotencyKey);

        using (new AssertionScope())
        {
            // Both attempts must succeed with 204. If the handler ran twice
            // the second would return 409 from the FSM (order already
            // Cancelled — see OrderingErrors.CannotCancelInStatus).
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);
            second.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Direct proof: the outbox publisher's domain-event handler
            // captured exactly one OrderCancelledEvent. A second handler
            // invocation would either capture a second event or fail the
            // FSM and capture none.
            fakeOutbox.GetMessages<OrderCancelledEvent>().Should().HaveCount(1,
                "the second POST must hit the idempotency cache, not the handler");

            var refreshed = await DbContext.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(OrderStatus.Cancelled);
        }
    }

    [Fact]
    public async Task WhenSameIdempotencyKeyUsedByDifferentBuyer_HandlerStillRuns()
    {
        // Pins ADR-0013's cross-buyer-partition guarantee against
        // FastEndpoints framework drift. The IdempotencyOptions exposed in
        // 7.0.1 has no AdditionalCacheKey property (the ADR's worked
        // example refers to ASP.NET OutputCache's AdditionalCacheKey, which
        // FastEndpoints' IdempotencyPolicy does not surface). The
        // partition is achieved via Authorization being in
        // IdempotencyOptions.AdditionalHeaders by default — different
        // bearer tokens => different cache vary-by => different cache
        // slots. If a future FastEndpoints minor drops Authorization from
        // the defaults, this test fails loudly instead of silently leaking
        // 204s across buyers.
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var ownerOrder = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var sharedKey = Guid.NewGuid().ToString();

        var ownerResponse = await PostCancelAsync(
            HttpClientRegistry.BuyerClient,
            ownerOrder.Id,
            "owner cancel",
            sharedKey);

        // Different buyer, same Idempotency-Key, same body. Must NOT
        // short-circuit to a cached 204 — that would leak cross-buyer.
        // Expected outcome: handler runs, ownership check fires, returns
        // 404 (existence-leak guard).
        var stranger = await PostCancelAsync(
            HttpClientRegistry.OtherBuyerClient,
            ownerOrder.Id,
            "owner cancel",
            sharedKey);

        using (new AssertionScope())
        {
            ownerResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            stranger.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the OutputCache key must vary by Authorization header so cross-buyer replays don't share slots");
        }
    }

    private static async Task<HttpResponseMessage> PostCancelAsync(
        HttpClient client,
        Guid orderId,
        string reason,
        string? idempotencyKey = null)
    {
        var body = new CancelOrderRequest { OrderId = orderId, Reason = reason };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ordering/orders/{orderId}/cancel")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
