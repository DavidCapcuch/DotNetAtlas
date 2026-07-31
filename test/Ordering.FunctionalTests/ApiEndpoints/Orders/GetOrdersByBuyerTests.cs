using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FastEndpoints;
using Microsoft.Extensions.Time.Testing;
using Ordering.Api.Endpoints.Orders.GetOrdersByBuyer;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection<FunctionalTestCollection>]
public class GetOrdersByBuyerTests : BaseApiTest
{
    private const string OrdersListRoute = "/api/v1/ordering/orders";

    // Positive control for the requiredness assertions — a path parameter is required by
    // construction, so it proves the document still expresses requiredness at all.
    private const string OrderByIdRoute = "/api/v1/ordering/orders/{orderId}";

    // ADR-0015: seed through a pinned clock so nothing in this class depends on wall-clock time.
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    public GetOrdersByBuyerTests(ApiTestFixture app)
        : base(app)
    {
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
        var ownA = await seed.CreateOrderAsync(TestUsers.BuyerId);
        var ownB = await seed.CreateOrderAsync(TestUsers.BuyerId);
        var someoneElses = await seed.CreateOrderAsync(TestUsers.OtherBuyerId);

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
                item.ItemCount.Should().Be(1);
                item.LastStatusChangeAtUtc.Should().Be(item.CreatedAtUtc);
            });
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
        await seed.CreateOrderAsync(TestUsers.BuyerId);

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

    private async Task<JsonNode> GetDocumentAsync()
    {
        var document = await HttpClientRegistry.BuyerClient.GetStringAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);

        return JsonNode.Parse(document)!;
    }
}
