using System.Net;
using System.Net.Http.Json;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.IntegrationTests.CrossCutting;

/// <summary>
/// Proves the threshold-crossing rule from <c>inventory.md</c> § 6.1:
/// <c>StockLevelChanged</c> fires ONLY on <c>0 &lt;-&gt; positive</c>
/// transitions, never on every stock movement. The rule sits on the publisher every stock mutation
/// reaches, so the sequence is driven through the slices' own entrances — receipts through the admin
/// endpoint, reservations through the saga's message.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class StockLevelChangedEmissionTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);

    public StockLevelChangedEmissionTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task OnlyFiresOnZeroToPositiveAndPositiveToZeroTransitions()
    {
        // Arrange — an initialized stream: Available stays 0 (never positive), so no emission yet.
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await Seed.InitializeAsync(productId, UtcNow, TestContext.Current.CancellationToken);

        // Act
        await ReceiveThroughAdminEndpointAsync(productId, quantity: 5); // 0 -> 5: EMIT
        await ReserveThroughSagaAsync(productId, orderId, quantity: 2); // 5 -> 3: no emission (positive to positive)
        await ReceiveThroughAdminEndpointAsync(productId, quantity: 1); // 3 -> 4: no emission
        await ReserveThroughSagaAsync(productId, orderId, quantity: 4); // 4 -> 0: EMIT

        // Assert
        var stockEventOutboxRows = await InventoryDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == productId.ToString()
                && m.TopicName == "inventory.stock-events"
                && m.Type == typeof(Inventory.Stock.StockLevelChangedEvent).FullName)
            .ToListAsync(TestContext.Current.CancellationToken);

        stockEventOutboxRows.Should().HaveCount(2,
            "exactly two 0<->positive transitions occurred across the sequence (0->5 and 4->0)");
    }

    private async Task ReceiveThroughAdminEndpointAsync(Guid productId, int quantity)
    {
        var response = await Fixture.HttpClientRegistry.CommandsClient.PostAsJsonAsync(
            $"/api/v1/inventory/stock-items/{productId}/receive",
            new { ProductId = productId, Quantity = quantity, Source = "receiving-dock" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task ReserveThroughSagaAsync(Guid productId, Guid orderId, int quantity)
    {
        var reservationId = Guid.NewGuid();

        // Stamped by the host's own clock, so each reserve lands after the receipts the host stamped.
        await Fixture.DispatchAsync<ReserveStockCommandKafkaHandler, AvroReserveStockCommand>(new AvroReserveStockCommand
        {
            OrderId = orderId,
            ProductId = productId,
            ReservationId = reservationId,
            Quantity = quantity,
            RequestedAtUtc = Fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime,
        });

        // A refused reserve does not throw at this entrance, so fail the precondition here rather than
        // let a shifted sequence surface as a wrong emission count.
        (await InventoryDbContext.ReservationAudit
            .AsNoTracking()
            .AnyAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken))
            .Should().BeTrue("every reserve in the sequence must succeed for the transitions to hold");
    }
}
