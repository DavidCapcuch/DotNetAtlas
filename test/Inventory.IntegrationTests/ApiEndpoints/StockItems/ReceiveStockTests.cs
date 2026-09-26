using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.IntegrationTests.ApiEndpoints.StockItems;

[Collection<IntegrationTestCollection>]
public sealed class ReceiveStockTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    public ReceiveStockTests(IntegrationTestFixture app)
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
        // Arrange
        var productId = Guid.CreateVersion7();
        await Seed.InitializeAsync(productId, SeedUtc, TestContext.Current.CancellationToken);
        var clock = Fixture.Services.GetRequiredService<TimeProvider>();
        var sentAfterUtc = clock.GetUtcNow();

        // Act
        var response = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 7), TestContext.Current.CancellationToken);
        var answeredBeforeUtc = clock.GetUtcNow();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();

        var projection = await InventoryDbContext.CurrentStockLevels
            .AsNoTracking()
            .SingleAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        using (new AssertionScope())
        {
            snapshot!.ProductId.Should().Be(productId);
            snapshot.OnHand.Should().Be(7);
            snapshot.Reserved.Should().Be(0);
            snapshot.Available.Should().Be(7);

            // The receive is the stream's second event (Initialize was the first), and the host's clock
            // stamped it inside the request.
            snapshot.LastVersion.Should().Be(2);
            snapshot.LastUpdatedUtc.Should().BeOnOrAfter(sentAfterUtc).And.BeOnOrBefore(answeredBeforeUtc);

            projection.OnHand.Should().Be(7);
            projection.Available.Should().Be(7);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenInvalidQuantity_Returns422()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        await Seed.InitializeAsync(productId, SeedUtc, TestContext.Current.CancellationToken);

        // Act
        var response = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/receive", BuildBody(productId, 0), TestContext.Current.CancellationToken);

        // Assert — both halves of the rejection: the 422, and no event appended past the initialize.
        var streamLength = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(e => e.StreamId == productId, TestContext.Current.CancellationToken);
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            streamLength.Should().Be(1);
        }
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
}
