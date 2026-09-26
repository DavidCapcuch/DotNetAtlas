using System.Net;
using System.Net.Http.Json;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.IntegrationTests.Journeys;

/// <summary>
/// The stock-availability journey — its one happy path. Stock received through the admin endpoint is
/// reservable by the saga at once, and the shopper-facing read then shows what is left. What is
/// asserted is the composition: the state between the steps, across two entrances (HTTP receive,
/// Kafka reserve) and the display read that fronts ADR-0034's cache.
/// </summary>
/// <remarks>
/// Example 2.4 of <c>docs/bc-design/example-mapping/inventory.md</c>: the reserve rehydrates the stream,
/// so a receipt that has only just landed is reservable straight away. That the reserve ignores a stale
/// projection or display cache is proven by <c>CrossCutting/StockLevelCacheBehaviorTests</c>.
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class StockAvailabilityJourneyTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    public StockAvailabilityJourneyTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task ReceiveReserveRead_FreshReceipt_IsReservableAtOnceAndShownNetOfTheReservation()
    {
        // Arrange — an initialized stream with nothing on hand.
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await Seed.InitializeAsync(productId, SeedUtc, TestContext.Current.CancellationToken);

        // Step 1 — the warehouse receives 10 through the admin endpoint; the display read shows them
        // (and warms the display cache).
        var receive = await Fixture.HttpClientRegistry.CommandsClient.PostAsJsonAsync(
            $"/api/v1/inventory/stock-items/{productId}/receive",
            new { ProductId = productId, Quantity = 10, Source = "receiving-dock" },
            TestContext.Current.CancellationToken);

        receive.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAvailableAsync(productId)).Should().Be(10);

        // Step 2 — the saga reserves 5 of them straight away, stamped by the host's own clock so the
        // stream stays in order behind the receipt.
        await Fixture.DispatchAsync<ReserveStockCommandKafkaHandler, AvroReserveStockCommand>(new AvroReserveStockCommand
        {
            OrderId = orderId,
            ProductId = productId,
            ReservationId = reservationId,
            Quantity = 5,
            RequestedAtUtc = Fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime,
        });

        var audit = await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .SingleAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        var stream = await InventoryDbContext.StockEvents
            .AsNoTracking()
            .Where(e => e.StreamId == productId)
            .OrderBy(e => e.Version)
            .Select(e => e.EventType)
            .ToListAsync(TestContext.Current.CancellationToken);
        using (new AssertionScope())
        {
            audit.Status.Should().Be(ReservationStatus.Active);
            audit.Quantity.Should().Be(5);
            stream.Should().Equal(
                nameof(StockItemInitializedDomainEvent),
                nameof(StockReceivedDomainEvent),
                nameof(StockReservedDomainEvent));
        }

        // Step 3 — the shopper-facing read shows the stock net of the reservation: the Kafka-driven
        // reserve evicted the display entry step 1 warmed.
        (await ReadAvailableAsync(productId)).Should().Be(5);
    }

    private async Task<int> ReadAvailableAsync(Guid productId)
    {
        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/stock-items/{productId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<StockLevelResponse>(TestContext.Current.CancellationToken);
        return snapshot!.Available;
    }
}
