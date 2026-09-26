using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.FunctionalTests.ApiEndpoints.StockItems;

[Collection<FunctionalTestCollection>]
public sealed class ReceiveStockTests : BaseApiTest
{
    public ReceiveStockTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAnonymous_Returns401()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 5), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenReadOnlyScope_Returns403()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 5), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenWriteScopeButNotAdmin_Returns403()
    {
        // Defense-in-depth: WritePolicy requires the admin role AND the inventory.write
        // scope. A token holding the scope but lacking the role must still be rejected —
        // this pins the role half so it can't be silently dropped.
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.WriteScopeNoAdminClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 5), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenCommandsScope_AndStreamInitialised_Returns200WithSnapshot()
    {
        var productId = Guid.CreateVersion7();
        await InitializeStreamAsync(productId);

        var response = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 7), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();
        using (new AssertionScope())
        {
            snapshot!.ProductId.Should().Be(productId);
            snapshot.OnHand.Should().Be(7);
            snapshot.Reserved.Should().Be(0);
            snapshot.Available.Should().Be(7);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenInvalidQuantity_Returns422()
    {
        var productId = Guid.CreateVersion7();
        await InitializeStreamAsync(productId);

        var response = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 0), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // [BindFrom("productId")] tells FastEndpoints to bind the route token, but
    // the request DTO still has `required Guid ProductId` for compile-time
    // discipline. System.Text.Json honours `required` on body deserialization,
    // so the JSON body must include ProductId — at runtime FE overrides it
    // with the route value, so route + body values agree.
    private static object BuildBody(Guid productId, int quantity) => new
    {
        ProductId = productId,
        Quantity = quantity,
        Source = "receiving-dock",
    };

    private async Task InitializeStreamAsync(Guid productId)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var init = scope.ServiceProvider.GetRequiredService<Platform.CQRS.ICommandHandler<InitializeStockItemCommand>>();
        var result = await init.HandleAsync(
            new InitializeStockItemCommand
            {
                ProductId = productId,
                OccurredOnUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
    }
}
