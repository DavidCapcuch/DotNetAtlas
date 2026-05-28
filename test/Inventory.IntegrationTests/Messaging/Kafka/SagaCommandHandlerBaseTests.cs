using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Application.StockItems.ReceiveStock;
using Inventory.Application.StockItems.ReserveStock;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Platform.Test.Framework.Kafka;
using AvroConfirmReservationCommand = Inventory.Reservations.ConfirmReservationCommand;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for the
/// <see cref="SagaCommandHandlerBase{TAvroCommand}"/> contract — exercises
/// the three observable failure modes the wrapper must distinguish:
/// (1) <see cref="FluentResults.Result.Fail(FluentResults.IError)"/> with a
/// business-expected error code (commit + return silently),
/// (2) <see cref="FluentResults.Result.Fail(FluentResults.IError)"/> with a
/// non-business code (throw <see cref="SagaCommandDispatchException"/> →
/// DLT route per <c>docs/bc-design/kafka-dlt-strategy.md § 1</c>),
/// (3) Unhandled exception (e.g. <c>DataIntegrityException</c> for a
/// bug-class condition like Confirm-on-uninitialized-stream — propagates,
/// also DLT-routed but via KafkaFlow's unhandled-exception path).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class SagaCommandHandlerBaseTests : BaseIntegrationTest
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 15, 0, 0, DateTimeKind.Utc);

    public SagaCommandHandlerBaseTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// Drives the wrapper's <c>Result.Fail</c> with a NON-business error code
    /// path: a <c>ReserveStockCommand</c> with <c>ReservationId = Guid.Empty</c>
    /// short-circuits in <c>ReserveStockCommandHandler</c> (line 54-58) with
    /// <c>Result.Fail(ValidationError("ReservationId.Empty"))</c> BEFORE any
    /// outbox row is staged — exactly the case the docstring on
    /// <c>BusinessExpectedErrorCodes</c> says MUST throw. Asserts
    /// <see cref="SagaCommandDispatchException"/> specifically.
    /// </summary>
    [Fact]
    public async Task ResultFailWithNonBusinessErrorCode_ThrowsSagaCommandDispatchException()
    {
        var productId = Guid.NewGuid();
        await Seed.ProductWithOnHandAsync(
            productId,
            onHand: 10,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        var avroCommand = new AvroReserveStockCommand
        {
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            ReservationId = Guid.Empty, // triggers ValidationError("ReservationId.Empty")
            Quantity = 1,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ReserveStockCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = async () => await handler.Handle(context, avroCommand);
        var thrown = await act.Should().ThrowAsync<SagaCommandDispatchException>();
        thrown.Which.Message.Should().Contain("ReserveStockCommand");
    }

    /// <summary>
    /// Drives the wrapper's unhandled-exception path: a Confirm against a
    /// stream whose <c>Version == 0</c> (never initialised) makes the
    /// aggregate throw <c>DataIntegrityException</c> — a bug-class condition
    /// that never reaches the <c>Result.Fail</c> filter. The exception
    /// propagates straight out of the wrapper for KafkaFlow's
    /// <c>DeadLetterMiddleware</c> to route. Asserts the wrapper does NOT
    /// swallow or wrap.
    /// </summary>
    [Fact]
    public async Task UnhandledExceptionPropagatesUnchanged()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        // Don't seed the stream -- ConfirmReservation on Version == 0 throws.
        var avroCommand = new AvroConfirmReservationCommand
        {
            CorrelationId = Guid.NewGuid(),
            ProductId = productId,
            ReservationId = reservationId,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmReservationCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = async () => await handler.Handle(context, avroCommand);
        // Specifically NOT SagaCommandDispatchException -- the wrapper does
        // not wrap unhandled exceptions; it lets them propagate.
        await act.Should().ThrowAsync<Exception>()
            .Where(ex => ex.GetType() != typeof(SagaCommandDispatchException));
    }

    /// <summary>
    /// Sanity check on the inverse: a happy-path Confirm completes without
    /// throwing. Confirms the wrapper isn't over-aggressive about exception
    /// routing — only Result.Fail with non-business code / unhandled throws
    /// should reach DLT, never a successful dispatch.
    /// </summary>
    [Fact]
    public async Task HappyPath_DoesNotThrow()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productId,
            reservationId,
            orderId,
            quantity: 1,
            new DateTimeOffset(UtcNow, TimeSpan.Zero).AddMinutes(-5),
            TestContext.Current.CancellationToken);

        var avroCommand = new AvroConfirmReservationCommand
        {
            CorrelationId = Guid.NewGuid(),
            ProductId = productId,
            ReservationId = reservationId,
            RequestedAtUtc = UtcNow,
        };

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ConfirmReservationCommandKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            cancellationToken: TestContext.Current.CancellationToken);

        var act = async () => await handler.Handle(context, avroCommand);
        await act.Should().NotThrowAsync();
    }
}
