using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.Events;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Platform.Test.Framework.Assertions;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.IntegrationTests.CrossCutting;

/// <summary>
/// ADR-0034's oversell guarantee at runtime, against the real <c>redis-cache</c>: the reservation
/// decision reads the event-sourced aggregate — never the display cache, nor the projection behind it —
/// so a display that overstates availability cannot cause a double-sell. The static half (the reserve
/// handler takes no dependency on the cache) is <c>StockAvailabilityCacheTests</c> in the architecture
/// suite.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class StockLevelCacheBehaviorTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 5, 2, 10, 0, 0, TimeSpan.Zero);

    public StockLevelCacheBehaviorTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task StaleCache_CannotCauseOversell()
    {
        // Arrange — 20 on hand, with the display cache warmed at that level.
        var productId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await Seed.ProductWithOnHandAsync(productId, onHand: 20, UtcNow, TestContext.Current.CancellationToken);
        (await ReadAvailableAsync(productId)).Should().Be(20);

        // A competing reservation of 15 lands on the stream as a raw append at V=3. No projection handler
        // runs for a raw row, so neither current_stock_levels nor the cached display entry hears of it:
        // the display now overstates availability by 15.
        await Fixture.InsertEventStoreRowAsync(
            productId,
            version: 3,
            @event: new StockReservedDomainEvent
            {
                ProductId = productId,
                ReservationId = Guid.CreateVersion7(),
                Quantity = 15,
                OrderId = Guid.CreateVersion7(),
                ExpiresAtUtc = UtcNow.AddMinutes(17),
                OccurredOnUtc = UtcNow.AddMinutes(2),
            },
            TestContext.Current.CancellationToken);

        // Act — the saga asks for 10: plenty by the display, too many by the stream (Available = 5).
        var reservationId = Guid.CreateVersion7();
        await Fixture.DispatchAsync<ReserveStockCommandKafkaHandler, AvroReserveStockCommand>(new AvroReserveStockCommand
        {
            OrderId = orderId,
            ProductId = productId,
            ReservationId = reservationId,
            Quantity = 10,
            RequestedAtUtc = UtcNow.AddMinutes(3).UtcDateTime,
        });

        // Assert — refused against the stream while the cached display still shows the stale level.
        var displayed = await ReadAvailableAsync(productId);
        var auditRow = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        var streamLength = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .CountAsync(e => e.StreamId == productId, TestContext.Current.CancellationToken);
        var outboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            RedisKeyExistsFor(productId).Should().BeTrue("the single display read goes through redis-cache");
            displayed.Should().Be(20, "the display is stale by construction — it never heard of the competing reserve");
            auditRow.Should().BeNull("the over-reservation was refused");
            streamLength.Should().Be(3, "nothing was appended past the competing reserve");
            outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations")
                .Which.Type.Should().BeMessageType<Inventory.Reservations.StockReservationFailedEvent>();
        }
    }

    private async Task<int> ReadAvailableAsync(Guid productId)
    {
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        return snapshot!.Available;
    }

    private bool RedisKeyExistsFor(Guid productId)
    {
        var server = Fixture.RedisMultiplexer.GetServer(Fixture.RedisMultiplexer.GetEndPoints()[0]);
        return server.Keys(pattern: $"*{productId:D}*").Any();
    }
}
