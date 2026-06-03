using FluentResults.Extensions.FluentAssertions;
using Ordering.Application.Orders.CreateOrder;
using Ordering.UnitTests.Application.Common;
using Platform.SharedKernel.Exceptions;

namespace Ordering.UnitTests.Application.Orders.CreateOrder;

public class CreateOrderCommandHandlerTests : HandlerTestBase
{
    private CreateOrderCommand ValidCommand(Guid? orderId = null, Guid? correlationId = null, Guid? buyerId = null) => new()
    {
        OrderId = orderId ?? Guid.CreateVersion7(),
        CorrelationId = correlationId ?? Guid.CreateVersion7(),
        BuyerId = buyerId ?? Guid.CreateVersion7(),
        PaymentMethodId = Guid.CreateVersion7(),
        Currency = "USD",
        Items = [new CreateOrderItemInput(Guid.CreateVersion7(), "SKU-1", "Prod", 2, 10m)],
        ShippingAddress = new AddressInput("1 Main", null, "Prague", null, "11000", "CZ"),
        BillingAddress = new AddressInput("2 Market", null, "Brno", null, "60200", "CZ"),
        RequestedAtUtc = TestAggregate.UtcNow,
    };

    private CreateOrderCommandHandler CreateHandler() =>
        new(DbContext, Logger<CreateOrderCommandHandler>());

    [Fact]
    public async Task Handle_HappyPath_CreatesOrderAndReturnsId()
    {
        var command = ValidCommand();

        var result = await CreateHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Should().NotBeEmpty();
        (await DbContext.Orders.FindAsync([result.Value], TestContext.Current.CancellationToken))
            .Should().NotBeNull();
    }

    /// <summary>
    /// ADR-0029 client-assigned identity: the handler persists the order under
    /// the pre-assigned <c>OrderId</c> the command carries (and returns it), so
    /// the saga's <c>CorrelationId == OrderId</c> round-trips. OrderId is
    /// deliberately distinct from CorrelationId here to prove the wiring.
    /// </summary>
    [Fact]
    public async Task Handle_PersistsSuppliedOrderId_AsAggregateIdentity()
    {
        var orderId = Guid.CreateVersion7();
        var command = ValidCommand(orderId: orderId);

        var result = await CreateHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        result.Value.Should().Be(orderId);
        var saved = await DbContext.Orders.FindAsync([orderId], TestContext.Current.CancellationToken);
        saved.Should().NotBeNull();
        saved!.CorrelationId.Should().Be(command.CorrelationId);
    }

    [Fact]
    public async Task Handle_ReplayWithSameCorrelationId_ReturnsExistingId_NoDuplicate()
    {
        var command = ValidCommand();
        var first = await CreateHandler().HandleAsync(command, TestContext.Current.CancellationToken);
        first.Should().BeSuccess();

        var second = await CreateHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        second.Should().BeSuccess();
        second.Value.Should().Be(first.Value);
        DbContext.Orders.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_InvalidCurrencyCode_ThrowsDataIntegrityException()
    {
        var command = ValidCommand() with { Currency = "QQQ" };

        var act = async () => await CreateHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DataIntegrityException>();
    }

    /// <summary>
    /// Pins ADR-0015 trace-fidelity: the saga-issued <c>RequestedAtUtc</c>
    /// becomes the order's <c>CreatedAtUtc</c>, not the handler-call wall
    /// clock. This keeps the saga's timeline coherent across Ordering's
    /// CreatedAt and the downstream OrderCreatedEvent.CreatedAtUtc payload.
    /// </summary>
    [Fact]
    public async Task Handle_UsesCommandRequestedAtUtc_AsOrderCreatedAtUtc_NotHandlerWallClock()
    {
        var requestedAt = new DateTimeOffset(2026, 5, 1, 12, 30, 0, TimeSpan.Zero);
        var command = ValidCommand() with { RequestedAtUtc = requestedAt };

        // Drift the handler's TimeProvider so we'd see the wrong value
        // if the handler still read TimeProvider.GetUtcNow().
        TimeProvider.SetUtcNow(requestedAt.AddHours(2));

        var result = await CreateHandler().HandleAsync(command, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        var saved = await DbContext.Orders.FindAsync(
            [result.Value],
            TestContext.Current.CancellationToken);
        saved.Should().NotBeNull();
        saved!.CreatedAtUtc.Should().Be(requestedAt);
    }
}
