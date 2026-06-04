using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.Events;
using Platform.SharedKernel.Errors;

namespace Ordering.UnitTests.Orders.Aggregates;

public class OrderCancelTests
{
    [Theory]
    [MemberData(nameof(CancellableStatuses))]
    public void Cancel_FromCancellableStatus_SucceedsAndRaisesEventWithAtStatus(OrderStatus from)
    {
        // Arrange
        var order = OrderTestFactory.OrderAt(from);
        var now = OrderTestFactory.UtcNow.AddHours(1);

        // Act
        var result = order.Cancel("buyer abandoned", now);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Cancelled);
            order.Cancellation.Should().NotBeNull();
            order.Cancellation!.Reason.Should().Be("buyer abandoned");
            order.Cancellation.AtStatus.Should().Be(from);
            order.Cancellation.CancelledAtUtc.Should().Be(now);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderCancelledDomainEvent>()
                .Subject;
            evt.AtStatus.Should().Be(from.Name);
            evt.Reason.Should().Be("buyer abandoned");
            evt.CancelledAtUtc.Should().Be(now);
            evt.BuyerId.Should().Be(order.BuyerId);
        }
    }

    public static TheoryData<OrderStatus> CancellableStatuses() => new()
    {
        OrderStatus.Created,
        OrderStatus.StockReserved,
        OrderStatus.PaymentCompleted,
        OrderStatus.Confirmed,
    };

    [Theory]
    [MemberData(nameof(NonCancellableStatuses))]
    public void Cancel_FromNonCancellableStatus_ReturnsFailureWithCannotCancelInStatus(OrderStatus from)
    {
        // Arrange
        var order = from == OrderStatus.Cancelled
            ? OrderTestFactory.CancelledOrder()
            : from == OrderStatus.Failed
                ? OrderTestFactory.FailedOrder()
                : OrderTestFactory.OrderAt(from);
        var before = order.Status;

        // Act
        var result = order.Cancel("buyer changed mind", OrderTestFactory.UtcNow.AddHours(1));

        // Assert (I-12)
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Order.CannotCancelInStatus");
            // M-4: the message must name the offending status so the HTTP
            // layer can render a meaningful 409 without inspecting the order.
            result.Errors.Should().ContainSingle(e => e.Message.Contains(from.Name));
            order.Status.Should().Be(before);
            order.PopDomainEvents().Should().BeEmpty();
        }
    }

    public static TheoryData<OrderStatus> NonCancellableStatuses() => new()
    {
        OrderStatus.Shipped,
        OrderStatus.Delivered,
        OrderStatus.Cancelled,
        OrderStatus.Failed,
    };
}
