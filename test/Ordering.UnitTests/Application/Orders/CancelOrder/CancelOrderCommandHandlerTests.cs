using FluentResults.Extensions.FluentAssertions;
using Ordering.Application.Orders.CancelOrder;
using Ordering.Domain.Orders;
using Ordering.UnitTests.Application.Common;

namespace Ordering.UnitTests.Application.Orders.CancelOrder;

public class CancelOrderCommandHandlerTests : HandlerTestBase
{
    private CancelOrderCommandHandler CreateHandler() =>
        new(DbContext, TimeProvider, Logger<CancelOrderCommandHandler>());

    [Fact]
    public async Task Handle_BuyerCancelsOwnOrder_Succeeds()
    {
        var buyerId = Guid.CreateVersion7();
        var order = TestAggregate.OrderAt(OrderStatus.StockReserved, buyerId);
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().HandleAsync(
            new CancelOrderCommand
            {
                OrderId = order.Id,
                Reason = "buyer abandoned",
                BuyerId = buyerId,
                IsAdmin = false,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Cancelled);
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_BuyerCancelsAnotherBuyersOrder_ReturnsNotFoundNotForbidden()
    {
        var owner = Guid.CreateVersion7();
        var intruder = Guid.CreateVersion7();
        var order = TestAggregate.OrderAt(OrderStatus.Created, owner);
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().HandleAsync(
            new CancelOrderCommand
            {
                OrderId = order.Id,
                Reason = "malicious",
                BuyerId = intruder,
                IsAdmin = false,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e => e.Message.Contains(order.Id.ToString()));
            order.Status.Should().Be(OrderStatus.Created);
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_AdminCancelsAnotherBuyersOrder_Succeeds()
    {
        var owner = Guid.CreateVersion7();
        var admin = Guid.CreateVersion7();
        var order = TestAggregate.OrderAt(OrderStatus.Confirmed, owner);
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().HandleAsync(
            new CancelOrderCommand
            {
                OrderId = order.Id,
                Reason = "operator override",
                BuyerId = admin,
                IsAdmin = true,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Cancelled);
        }
    }

    [Fact]
    public async Task Handle_CancelAfterShipped_ReturnsCannotCancelInStatus()
    {
        var buyer = Guid.CreateVersion7();
        var order = TestAggregate.OrderAt(OrderStatus.Shipped, buyer);
        DbContext.Orders.Add(order);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().HandleAsync(
            new CancelOrderCommand
            {
                OrderId = order.Id,
                Reason = "buyer changed mind",
                BuyerId = buyer,
                IsAdmin = false,
            },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e => e.Message.Contains(OrderStatus.Shipped.Name));
            order.Status.Should().Be(OrderStatus.Shipped);
        }
    }
}
