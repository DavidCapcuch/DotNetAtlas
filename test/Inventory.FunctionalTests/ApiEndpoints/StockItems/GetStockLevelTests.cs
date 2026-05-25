using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.FunctionalTests.ApiEndpoints.StockItems;

[Collection<FunctionalTestCollection>]
public sealed class GetStockLevelTests : BaseApiTest
{
    public GetStockLevelTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAnonymous_Returns401()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenReadOnlyScope_AndProductMissing_Returns404()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenReadOnlyScope_AndProductExists_Returns200()
    {
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 9);

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();
        snapshot!.OnHand.Should().Be(9);
    }

    [Fact]
    public async Task WhenCommandsScope_AlsoSatisfiesReadPolicy()
    {
        // Hierarchy check: Commands scope implies Read.
        var productId = Guid.CreateVersion7();
        await SeedStreamAsync(productId, onHand: 4);

        var response = await Fixture.HttpClientRegistry.CommandsClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

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
}
