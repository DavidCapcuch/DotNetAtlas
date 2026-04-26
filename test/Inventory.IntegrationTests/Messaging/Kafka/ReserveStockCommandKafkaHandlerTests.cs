using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// M5 acceptance for <see cref="ReserveStockCommandKafkaHandler"/>. The
/// Kafka handler is invoked directly with a synthetic
/// <see cref="FakeKafkaMessageContext"/> and an Avro
/// <see cref="AvroReserveStockCommand"/>; assertions cover the mapped
/// application command's side effects (event-store + projections + outbox)
/// — same observable surface as the M4 application-handler tests, but
/// driven through the Avro→app-command translation path.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ReserveStockCommandKafkaHandlerTests
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 10, 0, 0, DateTimeKind.Utc);

    private readonly IntegrationTestFixture _fixture;

    public ReserveStockCommandKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_AvroCommandTranslatedAndDispatched()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await SeedStreamAsync(productId, onHand: 10);

        var avroCommand = new AvroReserveStockCommand
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            ProductId = productId,
            ReservationId = reservationId,
            Quantity = 3,
            RequestedAtUtc = UtcNow,
        };

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ReserveStockCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(context, avroCommand);

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var audit = await db.ReservationAudit
            .AsNoTracking()
            .FirstAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        audit.Status.Should().Be(ReservationStatus.Active);
        audit.OrderId.Should().Be(orderId);
        audit.Quantity.Should().Be(3);

        var reserveRow = await db.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productId && r.EventType == "StockReservedEvent")
            .OrderByDescending(r => r.Version)
            .FirstAsync(TestContext.Current.CancellationToken);
        reserveRow.CorrelationId.Should().Be(correlationId);

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations");
    }

    [Fact]
    public async Task InsufficientStock_DoesNotThrowAndEmitsFailureEvent()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Seed only 2 units, then request 5 -> InsufficientStock.
        await SeedStreamAsync(productId, onHand: 2);

        var avroCommand = new AvroReserveStockCommand
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = productId,
            ReservationId = reservationId,
            Quantity = 5,
            RequestedAtUtc = UtcNow,
        };

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ReserveStockCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        // InsufficientStock is a business-expected outcome -> the handler
        // returns Result.Ok from the application layer (which itself wrote
        // the failure event to outbox). Therefore the SagaCommandHandlerBase
        // wrapper does NOT see Result.Fail and does NOT throw.
        var act = async () => await handler.Handle(context, avroCommand);
        await act.Should().NotThrowAsync();

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var auditRow = await db.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, TestContext.Current.CancellationToken);
        auditRow.Should().BeNull();

        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == orderId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRows.Should().ContainSingle(m => m.TopicName == "inventory.reservations");
    }

    private async Task SeedStreamAsync(Guid productId, int onHand)
    {
        var seedUtc = new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-2);

        using var seedScope = _fixture.CreateScope();
        var initHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<InitializeStockItemCommand>>();
        var receiveHandler = seedScope.ServiceProvider.GetRequiredService<ICommandHandler<ReceiveStockCommand, StockLevelResponse>>();

        await initHandler.HandleAsync(
            new InitializeStockItemCommand { ProductId = productId, OccurredOnUtc = seedUtc },
            TestContext.Current.CancellationToken);

        if (onHand > 0)
        {
            await receiveHandler.HandleAsync(
                new ReceiveStockCommand
                {
                    ProductId = productId,
                    Quantity = onHand,
                    Source = "receiving-dock",
                    ReceivedByUserId = null,
                    OccurredOnUtc = seedUtc.AddMinutes(1),
                },
                TestContext.Current.CancellationToken);
        }
    }
}
