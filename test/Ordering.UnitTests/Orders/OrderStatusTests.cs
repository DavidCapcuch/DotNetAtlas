using Ordering.Domain.Orders;

namespace Ordering.UnitTests.Orders;

public class OrderStatusTests
{
    public static TheoryData<OrderStatus, OrderStatus> AllowedTransitions() => new()
    {
        { OrderStatus.Created, OrderStatus.StockReserved },
        { OrderStatus.Created, OrderStatus.Cancelled },
        { OrderStatus.Created, OrderStatus.Failed },
        { OrderStatus.StockReserved, OrderStatus.PaymentCompleted },
        { OrderStatus.StockReserved, OrderStatus.Cancelled },
        { OrderStatus.StockReserved, OrderStatus.Failed },
        { OrderStatus.PaymentCompleted, OrderStatus.Confirmed },
        { OrderStatus.PaymentCompleted, OrderStatus.Cancelled },
        { OrderStatus.PaymentCompleted, OrderStatus.Failed },
        { OrderStatus.Confirmed, OrderStatus.Shipped },
        { OrderStatus.Confirmed, OrderStatus.Cancelled },
        { OrderStatus.Shipped, OrderStatus.Delivered },
    };

    public static TheoryData<OrderStatus, OrderStatus> DisallowedTransitions() => new()
    {
        // Skip states
        { OrderStatus.Created, OrderStatus.PaymentCompleted },
        { OrderStatus.Created, OrderStatus.Confirmed },
        { OrderStatus.Created, OrderStatus.Shipped },
        { OrderStatus.Created, OrderStatus.Delivered },
        { OrderStatus.StockReserved, OrderStatus.Confirmed },
        { OrderStatus.StockReserved, OrderStatus.Shipped },
        // Backwards
        { OrderStatus.StockReserved, OrderStatus.Created },
        { OrderStatus.PaymentCompleted, OrderStatus.StockReserved },
        { OrderStatus.Confirmed, OrderStatus.PaymentCompleted },
        { OrderStatus.Shipped, OrderStatus.Confirmed },
        // Self-transition (invalidated per spec)
        { OrderStatus.Created, OrderStatus.Created },
        { OrderStatus.StockReserved, OrderStatus.StockReserved },
        { OrderStatus.Shipped, OrderStatus.Shipped },
        // Confirmed cannot reach Failed (R4 — by then both stock + payment are green)
        { OrderStatus.Confirmed, OrderStatus.Failed },
        // Shipped post-ship restrictions
        { OrderStatus.Shipped, OrderStatus.Cancelled },
        { OrderStatus.Shipped, OrderStatus.Failed },
        // Terminal outbound — Delivered
        { OrderStatus.Delivered, OrderStatus.Shipped },
        { OrderStatus.Delivered, OrderStatus.Cancelled },
        // Terminal outbound — Cancelled
        { OrderStatus.Cancelled, OrderStatus.Created },
        { OrderStatus.Cancelled, OrderStatus.Confirmed },
        // Terminal outbound — Failed
        { OrderStatus.Failed, OrderStatus.Confirmed },
        { OrderStatus.Failed, OrderStatus.Cancelled },
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void CanTransitionTo_AllowedTransition_ReturnsTrue(OrderStatus from, OrderStatus to)
    {
        from.CanTransitionTo(to).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(DisallowedTransitions))]
    public void CanTransitionTo_DisallowedTransition_ReturnsFalse(OrderStatus from, OrderStatus to)
    {
        from.CanTransitionTo(to).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_NullTarget_Throws()
    {
        var act = () => OrderStatus.Created.CanTransitionTo(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsTerminal_TerminalStates_ReturnsTrue()
    {
        using (new AssertionScope())
        {
            OrderStatus.Delivered.IsTerminal.Should().BeTrue();
            OrderStatus.Cancelled.IsTerminal.Should().BeTrue();
            OrderStatus.Failed.IsTerminal.Should().BeTrue();
        }
    }

    [Fact]
    public void IsTerminal_NonTerminalStates_ReturnsFalse()
    {
        using (new AssertionScope())
        {
            OrderStatus.Created.IsTerminal.Should().BeFalse();
            OrderStatus.StockReserved.IsTerminal.Should().BeFalse();
            OrderStatus.PaymentCompleted.IsTerminal.Should().BeFalse();
            OrderStatus.Confirmed.IsTerminal.Should().BeFalse();
            OrderStatus.Shipped.IsTerminal.Should().BeFalse();
        }
    }

    [Fact]
    public void List_ContainsEightStates()
    {
        OrderStatus.List.Should().HaveCount(8);
    }
}
