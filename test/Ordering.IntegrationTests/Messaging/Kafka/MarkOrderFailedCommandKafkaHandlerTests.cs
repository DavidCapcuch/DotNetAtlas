using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common;
using Platform.SharedKernel.Exceptions;
using AvroMarkOrderFailedCommand = Ordering.Orders.MarkOrderFailedCommand;
using AvroOrderFailedEvent = Ordering.Orders.OrderFailedEvent;

namespace Ordering.IntegrationTests.Messaging.Kafka;

/// <summary>
/// M7 acceptance for <see cref="MarkOrderFailedCommandKafkaHandler"/>.
/// Per <c>example-mapping/ordering.md</c> Session 1 R4, <c>Failed</c> is
/// reachable from <c>{Created, StockReserved, PaymentCompleted}</c> but
/// <b>not</b> from <c>Confirmed</c> (by then both stock and payment are
/// green and no saga-driven failure path remains).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class MarkOrderFailedCommandKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public MarkOrderFailedCommandKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_FromCreated_TransitionsToFailedAndEmitsEvent()
    {
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, _fixture.FakeTime);
            var seeded = await seed.CreateOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
        }

        var fakeOutbox = _fixture.GetFakeOutbox();
        fakeOutbox.Clear();

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MarkOrderFailedCommandKafkaHandler>();
        var avro = new AvroMarkOrderFailedCommand
        {
            OrderId = orderId,
            CorrelationId = Guid.CreateVersion7(),
            ErrorCode = "STOCK_UNAVAILABLE",
            ErrorMessage = "Stock unavailable for one or more items.",
            RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
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
            saved.Status.Should().Be(OrderStatus.Failed);

            fakeOutbox.GetMessages<AvroOrderFailedEvent>()
                .Should().ContainSingle(m => m.IntegrationEvent.OrderId == orderId);
        }
    }

    /// <summary>
    /// Session 1 R4 — Confirmed is NOT a valid source for Failed.
    /// <c>Order.Fail</c> calls <c>GuardTransition</c> which throws
    /// <see cref="DataIntegrityException"/> (bug-class). The saga should
    /// never send <c>MarkOrderFailedCommand</c> against a Confirmed order
    /// — both stock and payment are already green by then. The throw
    /// propagates directly out of <see cref="SagaCommandHandlerBase{T}"/>
    /// (no Result.Fail wrapping) for KafkaFlow's
    /// <c>DeadLetterMiddleware</c> to route.
    /// </summary>
    [Fact]
    public async Task MarkFailedFromConfirmed_ThrowsDataIntegrityException()
    {
        Guid orderId;
        using (var seedScope = _fixture.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var seed = new OrderSeed(seedDb, _fixture.FakeTime);
            var seeded = await seed.CreateConfirmedOrderAsync(
                cancellationToken: TestContext.Current.CancellationToken);
            orderId = seeded.Id;
        }

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<MarkOrderFailedCommandKafkaHandler>();
        var avro = new AvroMarkOrderFailedCommand
        {
            OrderId = orderId,
            CorrelationId = Guid.CreateVersion7(),
            ErrorCode = "CONFIRMATION_TIMEOUT",
            ErrorMessage = "Should not happen post-Confirmed.",
            RequestedAtUtc = _fixture.FakeTime.GetUtcNow().UtcDateTime,
        };
        var ctx = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = () => handler.Handle(ctx, avro);
        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("Order.InvalidStatusTransition");
    }
}
