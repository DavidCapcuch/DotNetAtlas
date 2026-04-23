using FluentResults.Extensions.FluentAssertions;
using Ordering.Application.Orders.CreateOrder;
using Ordering.UnitTests.Application.Common;
using Platform.SharedKernel.Exceptions;

namespace Ordering.UnitTests.Application.Orders.CreateOrder;

public class CreateOrderCommandHandlerTests : HandlerTestBase
{
    private CreateOrderCommand ValidCommand(Guid? correlationId = null, Guid? buyerId = null) => new()
    {
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
        new(DbContext, TimeProvider, Logger<CreateOrderCommandHandler>());

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
}
