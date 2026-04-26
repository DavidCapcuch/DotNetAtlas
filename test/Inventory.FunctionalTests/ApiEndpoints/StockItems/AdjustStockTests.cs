using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.FunctionalTests.ApiEndpoints.StockItems;

[Collection<FunctionalTestCollection>]
public sealed class AdjustStockTests : BaseApiTest
{
    public AdjustStockTests(InventoryApiFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAnonymous_Returns401()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, -1), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenReadOnlyScope_Returns403()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, -1), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenCommandsScope_AndOnHandPositive_Returns200WithUpdatedSnapshot()
    {
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 10);

        var response = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, -3), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();
        snapshot!.OnHand.Should().Be(7);
    }

    [Fact]
    public async Task WhenSameIdempotencyKeyReplayed_BothCallsReturn200()
    {
        // Verifies the .Idempotency() filter wiring + output-cache pipeline
        // doesn't crash on a same-key replay. End-to-end proof of FE 7.0.1's
        // body-hash cache-key behavior (handler short-circuited on the second
        // call) is intentionally NOT asserted here — Basket accepted the same
        // limitation as an M8/M9 follow-up at
        // test/Basket.FunctionalTests/ApiEndpoints/Baskets/CheckoutBasketTests.cs:86-95
        // ("FE 7.0.0's body-hash cache key behavior wasn't reliably observed
        // in the test host for this BC; M9 can revisit"). The pipeline is wired
        // correctly per ADR-0013: AddIdempotencyKeyOutputCache (Redis-backed),
        // UseOutputCache before UseFastEndpoints, .Idempotency() on the
        // endpoint. Production verification stays manual until the FE
        // behavior is reliably reproducible in the test host.
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 20);

        var idempotencyKey = Guid.CreateVersion7().ToString();
        var body = BuildBody(productId, -2);
        var url = $"/api/v1/inventory/stock-items/{productId}/adjust";

        var client = Fixture.HttpClientRegistry.CommandsClientWithIdempotencyKey(idempotencyKey);

        var firstResponse = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResponse = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // [BindFrom("productId")] tells FastEndpoints to bind the route token, but
    // the request DTO still has `required Guid ProductId`. STJ honours `required`
    // on body deserialization, so the JSON body must include ProductId — at
    // runtime FE overrides it with the route value, so route + body agree.
    private static object BuildBody(Guid productId, int delta) => new
    {
        ProductId = productId,
        Delta = delta,
        Reason = "damage-write-off",
        AdjustedByUserId = Guid.CreateVersion7(),
    };

    private async Task SeedStreamAsync(Guid productId, int onHand)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var init = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<InitializeStockItemCommand>>();
        var receive = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();
        (await init.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-2) },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        (await receive.HandleAsync(
            new ReceiveStockCommand
            {
                ProductId = productId,
                Quantity = onHand,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
    }

    // TODO(M8): once FE 7.0.1's body-hash cache-key replay is reliably
    // reproducible inside WebApplicationFactory, strengthen
    // WhenSameIdempotencyKeyReplayed_BothCallsReturn200 to assert no second
    // StockAdjustedEvent appended on the second POST (count stock_events
    // before/after).
}
