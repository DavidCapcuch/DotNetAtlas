using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.Events;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;

namespace Inventory.IntegrationTests.ApiEndpoints.StockItems;

[Collection<IntegrationTestCollection>]
public sealed class AdjustStockTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    public AdjustStockTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAnonymous_Returns401()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, -1), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenReadOnlyScope_Returns403()
    {
        var productId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, -1), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenIdempotencyKeyMissing_Returns400()
    {
        // Arrange — ADR-0013 makes the Idempotency-Key header required here; CommandsClient sends none.
        // Seeded, so the adjustment would succeed if the handler ran.
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 10, SeedUtc, TestContext.Current.CancellationToken);

        // Act
        var response = await Fixture.HttpClientRegistry.CommandsClient
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, -1), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);

        // Assert — the documented rejection (use-cases.md § 4.2.2 AdjustStockCommand), and nothing adjusted.
        var adjustedEvents = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(StockAdjustedDomainEvent),
                TestContext.Current.CancellationToken);
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problem!.Detail.Should().Contain("Idempotency-Key");
            adjustedEvents.Should().Be(0);
        }
    }

    // The sign-specific arithmetic and its below-zero / below-reservations guards are owned by the
    // unit tier (StockItemTests.AdjustStock_*).
    [Theory]
    [InlineData(10, -3, 7)]
    [InlineData(4, 3, 7)]
    public async Task WhenCommandsScope_AndOnHandPositive_Returns200WithUpdatedSnapshot(
        int startOnHand, int delta, int expectedOnHand)
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, startOnHand, SeedUtc, TestContext.Current.CancellationToken);

        // ADR-0013 requires the Idempotency-Key header on this endpoint.
        var client = Fixture.HttpClientRegistry.CommandsClientWithIdempotencyKey(Guid.CreateVersion7().ToString());

        // Act
        var response = await client
            .PostAsJsonAsync($"/api/v1/inventory/stock-items/{productId}/adjust", BuildBody(productId, delta), TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();
        using (new AssertionScope())
        {
            snapshot!.OnHand.Should().Be(expectedOnHand);
            snapshot.Available.Should().Be(expectedOnHand);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task WhenSameIdempotencyKeyReplayed_AdjustsOnceAndReplaysTheFirstSnapshot()
    {
        // Arrange — one request body, because the body is part of the idempotency cache key.
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 20, SeedUtc, TestContext.Current.CancellationToken);

        var client = Fixture.HttpClientRegistry.CommandsClientWithIdempotencyKey(Guid.CreateVersion7().ToString());
        var body = BuildBody(productId, -2);
        var url = $"/api/v1/inventory/stock-items/{productId}/adjust";

        // Act
        var first = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        var replay = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);

        // Assert — ADR-0013: the replay is answered from the idempotency cache, so the handler runs once.
        var firstSnapshot = await first.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        var replaySnapshot = await replay.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        var adjustedEvents = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(StockAdjustedDomainEvent),
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            adjustedEvents.Should().Be(1, "the replay must not append a second adjustment");
            firstSnapshot!.OnHand.Should().Be(18);
            replaySnapshot.Should().BeEquivalentTo(firstSnapshot, "the replay returns the first response, not a fresh one");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenSameIdempotencyKeyUsedByAnotherAdmin_AdjustsAgain()
    {
        // Arrange — two admins (each keyed client carries its own token), one key, one body.
        var productId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 20, SeedUtc, TestContext.Current.CancellationToken);

        var idempotencyKey = Guid.CreateVersion7().ToString();
        var firstAdmin = Fixture.HttpClientRegistry.CommandsClientWithIdempotencyKey(idempotencyKey);
        var secondAdmin = Fixture.HttpClientRegistry.CommandsClientWithIdempotencyKey(idempotencyKey);
        var body = BuildBody(productId, -2);
        var url = $"/api/v1/inventory/stock-items/{productId}/adjust";

        // Act
        await firstAdmin.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        var second = await secondAdmin.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);

        // Assert — the caller's Authorization header is part of the cache key, so one admin's cached
        // response is never served to another: the second admin's adjustment runs.
        var secondSnapshot = await second.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        var adjustedEvents = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(
                e => e.StreamId == productId && e.EventType == nameof(StockAdjustedDomainEvent),
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            second.StatusCode.Should().Be(HttpStatusCode.OK);
            adjustedEvents.Should().Be(2);
            secondSnapshot!.OnHand.Should().Be(16);
        }
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
}
