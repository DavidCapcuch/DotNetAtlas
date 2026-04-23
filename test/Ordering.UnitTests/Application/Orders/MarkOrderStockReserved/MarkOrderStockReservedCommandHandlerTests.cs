using FluentResults.Extensions.FluentAssertions;
using Ordering.Application.Orders.MarkOrderStockReserved;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders;
using Ordering.UnitTests.Application.Common;
using Platform.SharedKernel.Exceptions;

namespace Ordering.UnitTests.Application.Orders.MarkOrderStockReserved;

public class MarkOrderStockReservedCommandHandlerTests : HandlerTestBase
{
    private MarkOrderStockReservedCommandHandler CreateHandler() =>
        new(DbContext, TimeProvider, Logger<MarkOrderStockReservedCommandHandler>());

    [Fact]
    public async Task Handle_HappyPath_TransitionsOrder()
    {
        var order = TestAggregate.NewOrder();
        _ = order.PopDomainEvents();
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().HandleAsync(
            new MarkOrderStockReservedCommand { OrderId = order.Id, ReservationId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        order.Status.Should().Be(OrderStatus.StockReserved);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsOrderNotFoundError()
    {
        var missingOrderId = Guid.CreateVersion7();

        var result = await CreateHandler().HandleAsync(
            new MarkOrderStockReservedCommand { OrderId = missingOrderId, ReservationId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e =>
            e.Message == OrderingErrors.OrderNotFound(missingOrderId).Message);
    }

    [Fact]
    public async Task Handle_IllegalTransition_ThrowsDataIntegrityException()
    {
        // Order already in Confirmed status; reserving stock again is FSM-violation (bug-class).
        var order = TestAggregate.OrderAt(OrderStatus.Confirmed);
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var act = async () => await CreateHandler().HandleAsync(
            new MarkOrderStockReservedCommand { OrderId = order.Id, ReservationId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DataIntegrityException>();
    }
}
