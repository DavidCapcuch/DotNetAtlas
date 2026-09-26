using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FastEndpoints;
using Microsoft.Extensions.Time.Testing;
using Ordering.Api.Endpoints.Orders.GetOrdersByBuyer;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.Domain.Orders;
using Ordering.IntegrationTests.Common;
using Ordering.IntegrationTests.Common.TestClientInfrastructure;
using Platform.SharedKernel.ValueObjects;
// FastEndpoints ships its own `Order` (endpoint ordering), which collides with the aggregate.
using Order = Ordering.Domain.Orders.Order;

namespace Ordering.IntegrationTests.ApiEndpoints.Orders;

/// <summary>
/// Slice tests for the buyer-orders list endpoint. Beyond auth and the paging contract, these pin
/// the SQL-side summary projection — in particular the <c>COALESCE</c> chain behind
/// <see cref="OrderSummaryDto.LastStatusChangeAtUtc"/>, which EF Core's InMemory provider cannot
/// translate, so the real Postgres container is what makes those assertions meaningful (ADR-0021).
/// </summary>
[Collection<IntegrationTestCollection>]
public class GetOrdersByBuyerTests : BaseIntegrationTest
{
    private const string OrdersListRoute = "/api/v1/ordering/orders";

    // Positive control for the requiredness assertions — a path parameter is required by
    // construction, so it proves the document still expresses requiredness at all.
    private const string OrderByIdRoute = "/api/v1/ordering/orders/{orderId}";

    // ADR-0015: seed through a pinned clock so nothing in this class depends on wall-clock time.
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    public GetOrdersByBuyerTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    /// <summary>
    /// A plain enum because <c>[InlineData]</c> needs compile-time constants and
    /// <see cref="OrderStatus"/> is a SmartEnum.
    /// </summary>
    public enum LifecycleState
    {
        Created,
        StockReserved,
        PaymentCompleted,
        Confirmed,
        Shipped,
        Delivered,
        Failed,
        Cancelled,
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest());

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task WhenBuyerHasOrders_ReturnsOnlyOwnOrdersAndPagingEnvelope()
    {
        var seed = new OrderSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var ownA = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);
        var ownB = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);
        var someoneElses = await seed.CreateOrderAsync(TestUsers.OtherBuyerId, cancellationToken: TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { PageNumber = 1, PageSize = 10 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Total.Should().Be(2);
            payload.PageNumber.Should().Be(1);
            payload.PageSize.Should().Be(10);
            payload.Items.Select(o => o.OrderId).Should()
                .BeEquivalentTo(new[] { ownA.Id, ownB.Id });
            payload.Items.Should().NotContain(o => o.OrderId == someoneElses.Id);
            payload.Items.Should().AllSatisfy(item =>
            {
                item.Status.Should().Be(OrderStatus.Created.Name);
                item.TotalAmount.Should().Be(19.98m);
                item.Currency.Should().Be(CurrencyCode.Eur.Name);
                item.ItemCount.Should().Be(1);
                // Against the pinned seed clock, not against the row's own CreatedAtUtc — a
                // self-comparison passes even if the projection dropped the column entirely.
                item.CreatedAtUtc.Should().Be(PinnedNow);
                item.LastStatusChangeAtUtc.Should().Be(PinnedNow);
            });
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenPagingPastTheFirstPage_ReturnsPartialLastPageAndUnboundedTotal()
    {
        // Total counts the whole result set, not the page — and the last page is short.
        var seed = new OrderSeed(DbContext, new FakeTimeProvider(PinnedNow));
        for (var i = 0; i < 5; i++)
        {
            await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);
        }

        var (firstResponse, firstPage) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { PageNumber = 1, PageSize = 2 });

        var (lastResponse, lastPage) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { PageNumber = 3, PageSize = 2 });

        using (new AssertionScope())
        {
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            firstPage.Total.Should().Be(5);
            firstPage.PageNumber.Should().Be(1);
            firstPage.PageSize.Should().Be(2);
            firstPage.Items.Should().HaveCount(2);

            lastResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            lastPage.Total.Should().Be(5);
            lastPage.PageNumber.Should().Be(3);
            lastPage.Items.Should().ContainSingle();
            lastPage.Items.Select(o => o.OrderId).Should()
                .NotIntersectWith(firstPage.Items.Select(o => o.OrderId));
        }
    }

    [Fact]
    public async Task WhenStatusFilterSupplied_ReturnsOnlyMatchingOrders()
    {
        const string reason = "Buyer requested cancellation";

        var seed = new OrderSeed(DbContext, new FakeTimeProvider(PinnedNow));
        await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken); // stays Created
        var cancelled = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);
        cancelled.Cancel(reason, PinnedNow.AddMinutes(1)).IsSuccess.Should().BeTrue();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest
                {
                    Status = OrderStatus.Cancelled.Name,
                    PageNumber = 1,
                    PageSize = 10,
                });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            // Total must reflect the filtered set, not the buyer's whole history.
            payload.Total.Should().Be(1);
            payload.Items.Should().ContainSingle();
            payload.Items[0].OrderId.Should().Be(cancelled.Id);
            payload.Items[0].Status.Should().Be(OrderStatus.Cancelled.Name);
        }
    }

    [Theory]
    [InlineData(LifecycleState.Created)]
    [InlineData(LifecycleState.StockReserved)]
    [InlineData(LifecycleState.PaymentCompleted)]
    [InlineData(LifecycleState.Confirmed)]
    [InlineData(LifecycleState.Shipped)]
    [InlineData(LifecycleState.Delivered)]
    [InlineData(LifecycleState.Failed)]
    [InlineData(LifecycleState.Cancelled)]
    public async Task WhenOrderAdvancedThroughLifecycle_LastStatusChangeAtUtcPicksMostRecentTimestamp(
        LifecycleState target)
    {
        var seeded = await SeedOrderAdvancingTimeAsync(target);
        var expected = ExpectedLastStatusChangeAtUtc(seeded, target);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { PageNumber = 1, PageSize = 10 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Items.Should().ContainSingle();
            // Exact equality, not BeCloseTo: each transition is a whole minute apart, so a
            // COALESCE branch picking the wrong field lands a minute off and fails loudly.
            payload.Items[0].LastStatusChangeAtUtc.Should().Be(expected);
            payload.Items[0].Status.Should().Be(ExpectedStatus(target).Name);
        }
    }

    /// <summary>
    /// Pins that the COALESCE chain behind <c>LastStatusChangeAtUtc</c> includes
    /// <c>Cancellation.CancelledAtUtc</c>. Without it, a Created-to-Cancelled row falls through to
    /// <c>CreatedAtUtc</c> and the buyer's list hides the cancellation.
    /// </summary>
    [Fact]
    [Trait("Category", "regression")]
    public async Task WhenOrderCancelledDirectlyFromCreated_LastStatusChangeAtUtcUsesCancelledAtUtc()
    {
        var fakeTime = new FakeTimeProvider(PinnedNow);
        var seed = new OrderSeed(DbContext, fakeTime);
        var seeded = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        seeded.Cancel("test cancellation", fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { PageNumber = 1, PageSize = 10 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Items.Should().ContainSingle();
            payload.Items[0].Status.Should().Be(OrderStatus.Cancelled.Name);
            payload.Items[0].LastStatusChangeAtUtc.Should().Be(seeded.Cancellation!.CancelledAtUtc);
            payload.Items[0].LastStatusChangeAtUtc.Should().NotBe(seeded.CreatedAtUtc);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenPagingParamsOmitted_ReturnsFirstPageOfTwenty()
    {
        // The server treats paging as optional and supplies 1/20 itself. Every other test
        // here passes both params explicitly, so nothing else pins that — and the OpenAPI
        // document is generated from the same members, so this is the behaviour the
        // document must agree with (ADR-0038).
        var seed = new OrderSeed(DbContext, new FakeTimeProvider(PinnedNow));
        await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        using var response = await HttpClientRegistry.BuyerClient.GetAsync(
            OrdersListRoute,
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GetOrdersByBuyerResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "omitting pageNumber/pageSize is legal — the server defaults them");
            payload!.PageNumber.Should().Be(1);
            payload.PageSize.Should().Be(20);
            payload.Total.Should().Be(1);
        }
    }

    [Fact]
    public async Task OpenApiDocument_DescribesPagingParamsAsOptional()
    {
        // ADR-0038 turns this document into the committed HTTP contract, read by oasdiff
        // and by generated clients. A parameter marked required that the server defaults
        // makes a generated client demand something the server never asked for.
        var document = await GetDocumentAsync();

        using (new AssertionScope())
        {
            // Positive control first. NSwag omits the `required` key entirely for an optional
            // parameter rather than emitting `required: false`, so every assertion below is
            // satisfied by requiredness disappearing from the document altogether — which is a
            // live risk, since ADR-0038 driver 4 plans the NSwag -> Microsoft.AspNetCore.OpenApi
            // move. A path parameter is required by construction; if this goes false the
            // mechanism is gone and the assertions below prove nothing.
            IsRequired(ParametersOf(document, OrderByIdRoute), "orderId").Should().BeTrue(
                "a path parameter is always required — this pins that the document still "
                + "expresses requiredness at all");

            var parameters = ParametersOf(document, OrdersListRoute);
            IsRequired(parameters, "pageNumber").Should().BeFalse();
            IsRequired(parameters, "pageSize").Should().BeFalse();
        }
    }

    [Theory]
    [Trait("Category", "boundary")]
    [InlineData("?pageSize=")]
    [InlineData("?pageSize=abc")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageNumber=0")]
    public async Task WhenPagingParamIsPresentButUnusable_RejectsRatherThanFallingBackToTheDefault(
        string queryString)
    {
        // An omitted param takes the default; a *supplied* one never does. `?pageSize=` binds to
        // 0 rather than null, so the endpoint's `??` deliberately does not fire and the value is
        // rejected by the validator — a caller's typo must not be silently served as page 1 of 20.
        using var response = await HttpClientRegistry.BuyerClient.GetAsync(
            OrdersListRoute + queryString,
            TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    [Fact]
    public async Task OpenApiDocument_PublishesThePagingDefaults()
    {
        // "Optional" alone leaves a consumer guessing what it gets by omitting the param.
        // The document carries the actual fallback so a generated client and its reader
        // both see it.
        var parameters = ParametersOf(await GetDocumentAsync(), OrdersListRoute);

        using (new AssertionScope())
        {
            DefaultOf(parameters, "pageNumber").Should().Be(1);
            DefaultOf(parameters, "pageSize").Should().Be(20);
        }
    }

    private static bool IsRequired(JsonArray parameters, string name)
        => ParameterNamed(parameters, name)["required"]?.GetValue<bool>() ?? false;

    private static int? DefaultOf(JsonArray parameters, string name)
        => ParameterNamed(parameters, name)["schema"]?["default"]?.GetValue<int>();

    private static JsonNode ParameterNamed(JsonArray parameters, string name)
        => parameters.SingleOrDefault(p => p!["name"]!.GetValue<string>() == name)
            ?? throw new InvalidOperationException(
                $"The document declares no '{name}' parameter. Declared: "
                + string.Join(", ", parameters.Select(p => p!["name"]!.GetValue<string>())));

    private static JsonArray ParametersOf(JsonNode document, string route)
        => document["paths"]![route]!["get"]!["parameters"]!.AsArray();

    private static DateTimeOffset ExpectedLastStatusChangeAtUtc(Order order, LifecycleState target) =>
        target switch
        {
            LifecycleState.Created => order.CreatedAtUtc,
            LifecycleState.StockReserved => order.StockReservedAtUtc!.Value,
            LifecycleState.PaymentCompleted => order.PaymentCompletedAtUtc!.Value,
            LifecycleState.Confirmed => order.ConfirmedAtUtc!.Value,
            LifecycleState.Shipped => order.Shipment!.ShippedAtUtc,
            LifecycleState.Delivered => order.DeliveredAtUtc!.Value,
            LifecycleState.Failed => order.Failure!.FailedAtUtc,
            LifecycleState.Cancelled => order.Cancellation!.CancelledAtUtc,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static OrderStatus ExpectedStatus(LifecycleState target) =>
        target switch
        {
            LifecycleState.Created => OrderStatus.Created,
            LifecycleState.StockReserved => OrderStatus.StockReserved,
            LifecycleState.PaymentCompleted => OrderStatus.PaymentCompleted,
            LifecycleState.Confirmed => OrderStatus.Confirmed,
            LifecycleState.Shipped => OrderStatus.Shipped,
            LifecycleState.Delivered => OrderStatus.Delivered,
            LifecycleState.Failed => OrderStatus.Failed,
            LifecycleState.Cancelled => OrderStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private async Task<JsonNode> GetDocumentAsync()
    {
        var document = await HttpClientRegistry.BuyerClient.GetStringAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);

        return JsonNode.Parse(document)!;
    }

    /// <summary>
    /// Walks a buyer order up to <paramref name="target"/>, one minute per transition, so every
    /// lifecycle timestamp is distinct.
    /// </summary>
    private async Task<Order> SeedOrderAdvancingTimeAsync(LifecycleState target)
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeTime = new FakeTimeProvider(PinnedNow);
        var seed = new OrderSeed(DbContext, fakeTime);

        var order = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: ct);
        if (target == LifecycleState.Created)
        {
            return order;
        }

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkStockReserved(Guid.CreateVersion7(), fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.StockReserved)
        {
            await DbContext.SaveChangesAsync(ct);
            return order;
        }

        // Terminal failure branches off after StockReserved — exercises the case where both
        // StockReservedAtUtc and Failure.FailedAtUtc are set and the chain must prefer the latter.
        if (target == LifecycleState.Failed)
        {
            fakeTime.Advance(TimeSpan.FromMinutes(1));
            order.Fail("TEST_FAIL", "test failure", fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
            await DbContext.SaveChangesAsync(ct);
            return order;
        }

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkPaymentCompleted(Guid.CreateVersion7(), fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.PaymentCompleted)
        {
            await DbContext.SaveChangesAsync(ct);
            return order;
        }

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        order.Confirm(fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.Confirmed)
        {
            await DbContext.SaveChangesAsync(ct);
            return order;
        }

        // Terminal cancellation branches off after Confirmed — Confirmed->Cancelled is the richest
        // pre-terminal happy path (I-12 blocks cancellation once Shipped), so this exercises the
        // chain preferring CancelledAtUtc over ConfirmedAtUtc and every earlier timestamp.
        if (target == LifecycleState.Cancelled)
        {
            fakeTime.Advance(TimeSpan.FromMinutes(1));
            order.Cancel("test cancellation", fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
            await DbContext.SaveChangesAsync(ct);
            return order;
        }

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkShipped("DHL", "1Z999AA10123456784", fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        if (target == LifecycleState.Shipped)
        {
            await DbContext.SaveChangesAsync(ct);
            return order;
        }

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        order.MarkDelivered(fakeTime.GetUtcNow()).IsSuccess.Should().BeTrue();
        await DbContext.SaveChangesAsync(ct);
        return order;
    }
}
