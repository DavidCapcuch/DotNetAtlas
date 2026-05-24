using Avro;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Orders.MarkOrderPaymentCompleted;
using Ordering.Application.Orders.MarkOrderStockReserved;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.CQRS;
using AvroConfirmOrderCommand = Ordering.Orders.ConfirmOrderCommand;
using AvroCreateOrderCommand = Ordering.Orders.CreateOrderCommand;
using AvroCreateOrderItem = Ordering.Orders.CreateOrderItem;
using AvroOrderAddress = Ordering.Orders.OrderAddress;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;
using AvroOrderCreatedEvent = Ordering.Orders.OrderCreatedEvent;

namespace Ordering.IntegrationTests.Sessions;

/// <summary>
/// <c>example-mapping/ordering.md</c> Session 1 Example 2 — the saga
/// drives an order along the canonical happy path
/// (<c>Created → StockReserved → PaymentCompleted → Confirmed</c>) and the
/// integration stack emits exactly two external events on the
/// <c>ordering.orders</c> topic: <c>OrderCreatedEvent</c> at creation and
/// <c>OrderConfirmedEvent</c> at confirmation. The intermediate
/// StockReserved and PaymentCompleted transitions are domain-internal in
/// v1 and have no external event (per the Avro schema inventory in
/// <c>events-catalog.md § 5.3</c>).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class HappyPathIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public HappyPathIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SagaDrivesOrderEndToEnd_AllStatusesAndTwoOutboxEvents()
    {
        var correlationId = Guid.CreateVersion7();
        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        // 1) Create — via Kafka handler.
        using (var scope = _fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<CreateOrderCommandKafkaHandler>();
            var avro = NewValidCreateCommand(correlationId);
            await handler.Handle(
                FakeKafkaMessageContext.Create(correlationId: correlationId, cancellationToken: TestContext.Current.CancellationToken),
                avro);
        }

        Guid orderId;
        using (var lookupScope = _fixture.CreateScope())
        {
            var db = lookupScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            orderId = (await db.Orders.AsNoTracking()
                .FirstAsync(o => o.CorrelationId == correlationId, TestContext.Current.CancellationToken)).Id;
        }

        // 2) MarkStockReserved — application handler only (no Kafka in v1).
        using (var scope = _fixture.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<MarkOrderStockReservedCommand>>();
            (await handler.HandleAsync(
                new MarkOrderStockReservedCommand
                {
                    OrderId = orderId,
                    ReservationId = Guid.CreateVersion7(),
                },
                TestContext.Current.CancellationToken))
                .IsSuccess.Should().BeTrue();
        }

        // 3) MarkPaymentCompleted — application handler only.
        using (var scope = _fixture.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<MarkOrderPaymentCompletedCommand>>();
            (await handler.HandleAsync(
                new MarkOrderPaymentCompletedCommand
                {
                    OrderId = orderId,
                    PaymentTransactionId = Guid.CreateVersion7(),
                },
                TestContext.Current.CancellationToken))
                .IsSuccess.Should().BeTrue();
        }

        // 4) Confirm — Kafka handler.
        using (var scope = _fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ConfirmOrderCommandKafkaHandler>();
            var avro = new AvroConfirmOrderCommand
            {
                OrderId = orderId,
                CorrelationId = correlationId,
                RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
            };
            await handler.Handle(
                FakeKafkaMessageContext.Create(correlationId: correlationId, cancellationToken: TestContext.Current.CancellationToken),
                avro);
        }

        using (new AssertionScope())
        using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var final = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);

            final.Status.Should().Be(OrderStatus.Confirmed);
            final.CreatedAtUtc.Should().NotBe(default);
            final.StockReservedAtUtc.Should().NotBeNull();
            final.PaymentCompletedAtUtc.Should().NotBeNull();
            final.ConfirmedAtUtc.Should().NotBeNull();

            // Exactly TWO external events: OrderCreated + OrderConfirmed.
            // StockReserved + PaymentCompleted are domain-internal only
            // (no Avro schemas exist for them).
            var created = fakeOutbox.GetMessages<AvroOrderCreatedEvent>()
                .Where(m => m.IntegrationEvent.OrderId == orderId)
                .ToList();
            created.Should().ContainSingle();
            created[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);

            var confirmed = fakeOutbox.GetMessages<AvroOrderConfirmedEvent>()
                .Where(m => m.IntegrationEvent.OrderId == orderId)
                .ToList();
            confirmed.Should().ContainSingle();
            confirmed[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
    }

    private AvroCreateOrderCommand NewValidCreateCommand(Guid correlationId) => new()
    {
        CorrelationId = correlationId,
        BuyerId = Guid.CreateVersion7(),
        PaymentMethodId = Guid.CreateVersion7(),
        Items = new List<AvroCreateOrderItem>
        {
            new()
            {
                ProductId = Guid.CreateVersion7(),
                Sku = "SKU-EE2E",
                Name = "End-to-end widget",
                Quantity = 1,
                UnitPriceAmount = new AvroDecimal(15m),
                UnitPriceCurrency = "EUR",
            },
        },
        ShippingAddress = NewAvroAddress(),
        BillingAddress = NewAvroAddress(),
        RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
    };

    private static AvroOrderAddress NewAvroAddress() => new()
    {
        Street1 = "1 Saga Street",
        Street2 = null,
        City = "Prague",
        State = null,
        PostalCode = "11000",
        CountryCode = "CZ",
    };
}
