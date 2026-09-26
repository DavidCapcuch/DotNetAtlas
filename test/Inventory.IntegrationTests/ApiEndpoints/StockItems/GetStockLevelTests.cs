using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using Inventory.Application.StockItems.Common;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;

namespace Inventory.IntegrationTests.ApiEndpoints.StockItems;

/// <summary>
/// Integration coverage for <c>GET /api/v1/inventory/stock-items/{productId}</c> — the single
/// stock-availability read. <c>AllowAnonymous</c> per use-cases.md § 4.4.1 + ADR-0034: it is the
/// public product-page availability overlay, the same posture as its bulk sibling
/// (<c>POST /stock-items/bulk</c>). Anonymous shoppers read availability; token-bearing callers
/// (BFF / service-to-service) are equally allowed.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetStockLevelTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    public GetStockLevelTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAnonymous_AndProductExists_Returns200WithSnapshot()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 9, SeedUtc, TestContext.Current.CancellationToken);

        // Act
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();
        using (new AssertionScope())
        {
            snapshot!.OnHand.Should().Be(9);
            snapshot.ProductId.Should().Be(productId);
            snapshot.Available.Should().Be(9);
        }
    }

    [Fact]
    public async Task WhenAnonymous_AndProductMissing_Returns404()
    {
        // Arrange
        var productId = Guid.CreateVersion7();

        // Act
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // The domain error code is what tells a caller "no stock item" apart from an unrouted path.
            problem!.Errors.Should().ContainSingle(e => e.Code == "Inventory.StockItem.NotFound");
        }
    }

    [Fact]
    public async Task WhenAuthenticated_AlsoReturns200()
    {
        // AllowAnonymous does not exclude token-bearing callers — a BFF / service-to-service
        // request carrying a JWT reads availability just the same.
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 4, SeedUtc, TestContext.Current.CancellationToken);

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RepeatRead_IsServedFromCache_NotTheProjectionRow()
    {
        // Arrange — warm the read-through cache, then change the projection row behind it. A raw
        // UPDATE appends no stock event, so nothing evicts the cached entry.
        var productId = Guid.CreateVersion7();
        var url = $"/api/v1/inventory/stock-items/{productId}";
        await Seed.ProductWithOnHandAsync(productId, onHand: 9, SeedUtc, TestContext.Current.CancellationToken);
        await Fixture.HttpClientRegistry.NonAuthClient.GetAsync(url, TestContext.Current.CancellationToken);

        var updatedRows = await InventoryDbContext.CurrentStockLevels
            .Where(r => r.ProductId == productId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.OnHand, 2).SetProperty(r => r.Available, 2),
                TestContext.Current.CancellationToken);
        updatedRows.Should().Be(1, "the projection row must change behind the cache");

        // Act
        var snapshot = await Fixture.HttpClientRegistry.NonAuthClient
            .GetFromJsonAsync<StockLevelResponse>(url, TestContext.Current.CancellationToken);

        // Assert — ADR-0034: inside the TTL the warmed entry answers, not current_stock_levels.
        using (new AssertionScope())
        {
            snapshot!.OnHand.Should().Be(9);
            snapshot.Available.Should().Be(9);
        }
    }
}
