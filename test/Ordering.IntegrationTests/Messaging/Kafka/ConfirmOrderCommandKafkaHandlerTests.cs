using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Orders.MarkOrderPaymentCompleted;
using Ordering.Application.Orders.MarkOrderStockReserved;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;
using AvroConfirmOrderCommand = Ordering.Orders.ConfirmOrderCommand;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;

namespace Ordering.IntegrationTests.Messaging.Kafka;

/// <summary>
/// M7 acceptance for <see cref="ConfirmOrderCommandKafkaHandler"/>.
/// Covers <c>example-mapping/ordering.md</c> Session 1 Example 4
/// (backwards-walk Confirm-on-Shipped) and the Example-1-spirit case
/// (Confirm against an order that hasn't reserved stock + paid yet —
/// which the FSM also rejects).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ConfirmOrderCommandKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public ConfirmOrderCommandKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_ConfirmedFromPaymentCompleted_EmitsOrderConfirmedEvent()
    {
        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        // Seed a Created order, then walk it to PaymentCompleted via the
        // application handlers (those two transitions have no Kafka
        // handler in v1 — saga drives them via direct app-command
        // dispatch).
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, TimeProvider.System);
            var seeded = await seed.CreateOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;

            var stockReservedHandler = seedScope.ServiceProvider
                .GetRequiredService<ICommandHandler<MarkOrderStockReservedCommand>>();
            var paymentCompletedHandler = seedScope.ServiceProvider
                .GetRequiredService<ICommandHandler<MarkOrderPaymentCompletedCommand>>();

            (await stockReservedHandler.HandleAsync(
                new MarkOrderStockReservedCommand
                {
                    OrderId = orderId,
                    ReservationId = Guid.CreateVersion7(),
                },
                TestContext.Current.CancellationToken))
                .IsSuccess.Should().BeTrue();

            (await paymentCompletedHandler.HandleAsync(
                new MarkOrderPaymentCompletedCommand
                {
                    OrderId = orderId,
                    PaymentTransactionId = Guid.CreateVersion7(),
                },
                TestContext.Current.CancellationToken))
                .IsSuccess.Should().BeTrue();
        }

        // Internal StockReserved + PaymentCompleted transitions are NOT
        // exposed as external events, so clear here so the post-Confirm
        // assertion only counts the Confirm emission.
        fakeOutbox.Clear();

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmOrderCommandKafkaHandler>();
        var avro = new AvroConfirmOrderCommand
        {
            OrderId = orderId,
            CorrelationId = Guid.CreateVersion7(),
            RequestedAtUtc = DateTime.UtcNow,
        };
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(ctx, avro);

        using var verifyScope = _fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        using (new AssertionScope())
        {
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            saved.Status.Should().Be(OrderStatus.Confirmed);

            fakeOutbox.GetMessages<AvroOrderConfirmedEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == orderId);
        }
    }

    /// <summary>
    /// Session 1 Example 4 — backwards-walk attempt. Saga retry sends
    /// Confirm against an already-Shipped order. <c>Order.Confirm</c> calls
    /// <c>GuardTransition</c> which throws <see cref="DataIntegrityException"/>
    /// — FSM violations are bug-class (the saga should never send invalid
    /// transitions), they bypass the <c>Result.Fail</c> path entirely.
    /// <see cref="SagaCommandHandlerBase{T}"/> does NOT wrap unhandled
    /// exceptions; the throw propagates directly into KafkaFlow's
    /// <c>DeadLetterMiddleware</c>.
    /// </summary>
    [Fact]
    public async Task ConfirmFromShipped_ThrowsDataIntegrityException()
    {
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, TimeProvider.System);
            var seeded = await seed.CreateShippedOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
        }

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmOrderCommandKafkaHandler>();
        var avro = new AvroConfirmOrderCommand
        {
            OrderId = orderId,
            CorrelationId = Guid.CreateVersion7(),
            RequestedAtUtc = DateTime.UtcNow,
        };
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = () => handler.Handle(ctx, avro);
        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Order.InvalidStatusTransition");
    }

    /// <summary>
    /// Session 1 Example 1 (skip-stock-reservation) at the integration level.
    /// Saga sends Confirm to a Created order without first walking through
    /// StockReserved + PaymentCompleted. Same FSM violation path as the
    /// Shipped case — <see cref="DataIntegrityException"/> from
    /// <c>GuardTransition</c>.
    /// </summary>
    [Fact]
    public async Task ConfirmFromCreated_ThrowsDataIntegrityException()
    {
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, TimeProvider.System);
            var seeded = await seed.CreateOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
        }

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmOrderCommandKafkaHandler>();
        var avro = new AvroConfirmOrderCommand
        {
            OrderId = orderId,
            CorrelationId = Guid.CreateVersion7(),
            RequestedAtUtc = DateTime.UtcNow,
        };
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = () => handler.Handle(ctx, avro);
        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Order.InvalidStatusTransition");
    }
}
