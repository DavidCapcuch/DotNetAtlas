using FluentResults.Extensions.FluentAssertions;
using Ordering.Application.Orders.GetOrderById;
using Ordering.Domain.Orders;
using Ordering.UnitTests.Application.Common;

namespace Ordering.UnitTests.Application.Orders.GetOrderById;

/// <summary>
/// Exercises the handler's authorization + not-found branches. The success
/// path that projects <see cref="Order"/> → <c>GetOrderByIdResponse</c> is
/// exercised by Application integration tests against the real
/// <c>OrderingDbContext</c> (M4) — the test-project's InMemory context
/// intentionally ignores VO / SmartEnum / owned-type properties to keep M3
/// scope inside the Application layer.
/// </summary>
public class GetOrderByIdQueryHandlerTests : HandlerTestBase
{
    private GetOrderByIdQueryHandler CreateHandler() =>
        new(DbContext, Logger<GetOrderByIdQueryHandler>());

    [Fact]
    public async Task Handle_BuyerAsksForAnotherBuyersOrder_ReturnsNotFound_NotForbidden()
    {
        var owner = Guid.CreateVersion7();
        var intruder = Guid.CreateVersion7();
        var order = TestAggregate.OrderAt(OrderStatus.Created, owner);
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().HandleAsync(
            new GetOrderByIdQuery { OrderId = order.Id, BuyerId = intruder, IsAdmin = false },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(order.Id.ToString()));
    }

    [Fact]
    public async Task Handle_MissingOrder_ReturnsNotFound()
    {
        var missing = Guid.CreateVersion7();

        var result = await CreateHandler().HandleAsync(
            new GetOrderByIdQuery { OrderId = missing, BuyerId = Guid.CreateVersion7(), IsAdmin = true },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(missing.ToString()));
    }
}
