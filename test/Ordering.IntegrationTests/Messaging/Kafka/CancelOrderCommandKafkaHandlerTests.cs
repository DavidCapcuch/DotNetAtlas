using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.Test.Framework.Kafka;
using AvroCancelOrderCommand = Ordering.Orders.CancelOrderCommand;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroOrderStatusAtTransition = Ordering.Orders.OrderStatusAtTransition;

namespace Ordering.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for <see cref="CancelOrderCommandKafkaHandler"/> covering
/// every example in <c>example-mapping/ordering.md</c> Session 2.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class CancelOrderCommandKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public CancelOrderCommandKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Session 2 Example 1 — cancel before stock reservation.
    /// </summary>
    [Fact]
    [Trait("Category", "critical-path")]
    public async Task CancelFromCreated_EmitsOrderCancelledEvent_AtStatusCreated()
    {
        Guid orderId;
        Guid buyerId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, TimeProvider.System);
            var seeded = await seed.CreateOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
            buyerId = seeded.BuyerId;
        }

        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        await DispatchCancelAsync(orderId, "buyer abandoned");

        using (new AssertionScope())
        using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            saved.Status.Should().Be(OrderStatus.Cancelled);

            var captured = fakeOutbox.GetMessages<AvroOrderCancelledEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == orderId).Subject;
            captured.IntegrationEvent.AtStatus.Should().Be(AvroOrderStatusAtTransition.Created);
            captured.IntegrationEvent.BuyerId.Should().Be(buyerId);
            captured.IntegrationEvent.Reason.Should().Be("buyer abandoned");
        }
    }

    /// <summary>
    /// Session 2 Example 2 — admin cancels a confirmed order.
    /// </summary>
    [Fact]
    [Trait("Category", "critical-path")]
    public async Task CancelFromConfirmed_EmitsOrderCancelledEvent_AtStatusConfirmed()
    {
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, TimeProvider.System);
            var seeded = await seed.CreateConfirmedOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
        }

        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        await DispatchCancelAsync(orderId, "operator override");

        using (new AssertionScope())
        using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            saved.Status.Should().Be(OrderStatus.Cancelled);

            var captured = fakeOutbox.GetMessages<AvroOrderCancelledEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == orderId).Subject;
            captured.IntegrationEvent.AtStatus.Should().Be(AvroOrderStatusAtTransition.Confirmed);
        }
    }

    /// <summary>
    /// Session 2 Example 3 — cancellation attempted after shipping.
    /// <c>Order.Cancel</c> returns <c>Result.Fail(OrderingErrors.CannotCancelInStatus)</c>;
    /// <see cref="SagaCommandHandlerBase{T}"/> wraps that into
    /// <see cref="SagaCommandDispatchException"/> for DLT routing. Status
    /// must remain <c>Shipped</c> and no <c>OrderCancelledEvent</c> may be
    /// emitted.
    /// </summary>
    [Fact]
    public async Task CancelFromShipped_ThrowsSagaCommandDispatchException_NoEventEmitted()
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

        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        var act = () => DispatchCancelAsync(orderId, "buyer changed mind");
        await act.Should().ThrowAsync<SagaCommandDispatchException>();

        using (new AssertionScope())
        using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            saved.Status.Should().Be(OrderStatus.Shipped);

            fakeOutbox.GetMessages<AvroOrderCancelledEvent>()
                .Where(m => m.IntegrationEvent.OrderId == orderId)
                .Should().BeEmpty();
        }
    }

    /// <summary>
    /// Session 2 Example 4 — cancellation attempted after delivery. Same
    /// rejection path as Shipped (both are post-ship states).
    /// </summary>
    [Fact]
    public async Task CancelFromDelivered_ThrowsSagaCommandDispatchException_NoEventEmitted()
    {
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, TimeProvider.System);
            var seeded = await seed.CreateDeliveredOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
        }

        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        var act = () => DispatchCancelAsync(orderId, "buyer dispute");
        await act.Should().ThrowAsync<SagaCommandDispatchException>();

        using (new AssertionScope())
        using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var saved = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId, TestContext.Current.CancellationToken);
            saved.Status.Should().Be(OrderStatus.Delivered);

            fakeOutbox.GetMessages<AvroOrderCancelledEvent>()
                .Where(m => m.IntegrationEvent.OrderId == orderId)
                .Should().BeEmpty();
        }
    }

    private async Task DispatchCancelAsync(Guid orderId, string reason)
    {
        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CancelOrderCommandKafkaHandler>();
        var avro = new AvroCancelOrderCommand
        {
            OrderId = orderId,
            Reason = reason,
            RequestedAtUtc = DateTime.UtcNow,
        };
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(ctx, avro);
    }
}
