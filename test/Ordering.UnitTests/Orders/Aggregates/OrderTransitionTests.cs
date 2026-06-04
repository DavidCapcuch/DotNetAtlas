using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.Events;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Ordering.UnitTests.Orders.Aggregates;

public class OrderTransitionTests
{
    [Fact]
    public void MarkStockReserved_FromCreated_AdvancesStatusAndRaisesEvent()
    {
        // Arrange
        var order = OrderTestFactory.OrderAt(OrderStatus.Created);
        var reservationId = Guid.CreateVersion7();
        var now = OrderTestFactory.UtcNow.AddMinutes(1);

        // Act
        var result = order.MarkStockReserved(reservationId, now);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.StockReserved);
            order.StockReservationId.Should().Be(reservationId);
            order.StockReservedAtUtc.Should().Be(now);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderStockReservedDomainEvent>()
                .Subject;
            evt.OrderId.Should().Be(order.Id);
            evt.ReservationId.Should().Be(reservationId);
            evt.OccurredOnUtc.Should().Be(now);
        }
    }

    [Fact]
    public void MarkPaymentCompleted_FromStockReserved_AdvancesAndRaisesEvent()
    {
        var order = OrderTestFactory.OrderAt(OrderStatus.StockReserved);
        var txId = Guid.CreateVersion7();
        var now = OrderTestFactory.UtcNow.AddMinutes(2);

        var result = order.MarkPaymentCompleted(txId, now);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.PaymentCompleted);
            order.PaymentTransactionId.Should().Be(txId);
            order.PaymentCompletedAtUtc.Should().Be(now);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderPaymentCompletedDomainEvent>()
                .Subject;
            evt.PaymentTransactionId.Should().Be(txId);
        }
    }

    [Fact]
    public void Confirm_FromPaymentCompleted_AdvancesAndRaisesEvent()
    {
        var order = OrderTestFactory.OrderAt(OrderStatus.PaymentCompleted);
        var now = OrderTestFactory.UtcNow.AddMinutes(3);

        var result = order.Confirm(now);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Confirmed);
            order.ConfirmedAtUtc.Should().Be(now);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderConfirmedDomainEvent>()
                .Subject;
            evt.BuyerId.Should().Be(order.BuyerId);
        }
    }

    [Fact]
    public void MarkShipped_FromConfirmed_AdvancesPersistsShipmentAndRaisesEvent()
    {
        var order = OrderTestFactory.OrderAt(OrderStatus.Confirmed);
        var now = OrderTestFactory.UtcNow.AddMinutes(4);

        var result = order.MarkShipped("DHL", "TRK-42", now);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Shipped);
            order.Shipment.Should().NotBeNull();
            order.Shipment!.Carrier.Should().Be("DHL");
            order.Shipment.TrackingNumber.Should().Be("TRK-42");
            order.Shipment.ShippedAtUtc.Should().Be(now);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderShippedDomainEvent>()
                .Subject;
            evt.Carrier.Should().Be("DHL");
            evt.TrackingNumber.Should().Be("TRK-42");
            evt.ShippedAtUtc.Should().Be(now);
        }
    }

    [Fact]
    public void MarkDelivered_FromShipped_AdvancesPersistsTimestampAndRaisesEvent()
    {
        var order = OrderTestFactory.OrderAt(OrderStatus.Shipped);
        var now = OrderTestFactory.UtcNow.AddMinutes(5);

        var result = order.MarkDelivered(now);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Delivered);
            order.DeliveredAtUtc.Should().Be(now);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderDeliveredDomainEvent>()
                .Subject;
            evt.DeliveredAtUtc.Should().Be(now);
        }
    }

    [Fact]
    public void Fail_FromStockReserved_AdvancesAndRaisesEventWithAtStatusPreserved()
    {
        var order = OrderTestFactory.OrderAt(OrderStatus.StockReserved);
        var now = OrderTestFactory.UtcNow.AddMinutes(2);

        var result = order.Fail("PAYMENT_FAILED", "Card declined.", now);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            order.Status.Should().Be(OrderStatus.Failed);
            order.Failure.Should().NotBeNull();
            order.Failure!.ErrorCode.Should().Be("PAYMENT_FAILED");
            order.Failure.AtStatus.Should().Be(OrderStatus.StockReserved);

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderFailedDomainEvent>()
                .Subject;
            evt.AtStatus.Should().Be(OrderStatus.StockReserved.Name);
            evt.ErrorCode.Should().Be("PAYMENT_FAILED");
            evt.ErrorMessage.Should().Be("Card declined.");
        }
    }

    [Fact]
    public void MarkPaymentCompleted_FromCreated_ThrowsDataIntegrityException()
    {
        // Session 1: saga tries to skip stock reservation
        var order = OrderTestFactory.OrderAt(OrderStatus.Created);
        var before = order.Status;

        var act = () => order.MarkPaymentCompleted(Guid.CreateVersion7(), OrderTestFactory.UtcNow);

        using (new AssertionScope())
        {
            act.Should().Throw<DataIntegrityException>()
                .WithMessage("*Cannot transition*Created*PaymentCompleted*");
            order.Status.Should().Be(before);
        }
    }

    [Fact]
    public void Confirm_FromShipped_ThrowsDataIntegrityException()
    {
        // Session 1: walk backwards
        var order = OrderTestFactory.OrderAt(OrderStatus.Shipped);

        var act = () => order.Confirm(OrderTestFactory.UtcNow);

        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Shipped*Confirmed*");
    }

    [Fact]
    public void MarkShipped_FromDelivered_ThrowsDataIntegrityException()
    {
        // I-11 terminal: Delivered has no outbound transitions
        var order = OrderTestFactory.OrderAt(OrderStatus.Delivered);

        var act = () => order.MarkShipped("DHL", "TRK-99", OrderTestFactory.UtcNow);

        act.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void Fail_FromConfirmed_ThrowsDataIntegrityException()
    {
        // Session 1 R4: Confirmed cannot reach Failed
        var order = OrderTestFactory.OrderAt(OrderStatus.Confirmed);

        var act = () => order.Fail("POST_CONFIRM_BLOWUP", "nope", OrderTestFactory.UtcNow);

        act.Should().Throw<DataIntegrityException>();
    }

    [Fact]
    public void MarkStockReserved_EmptyReservationId_ThrowsDataIntegrityException()
    {
        var order = OrderTestFactory.OrderAt(OrderStatus.Created);

        var act = () => order.MarkStockReserved(Guid.Empty, OrderTestFactory.UtcNow);

        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*ReservationId*");
    }

    [Fact]
    public void HappyPath_CreatedThroughDelivered_RaisesSixEventsInOrder()
    {
        // Arrange
        var order = OrderTestFactory.NewOrder();

        // Act — full lifecycle
        order.MarkStockReserved(Guid.CreateVersion7(), OrderTestFactory.UtcNow.AddMinutes(1));
        order.MarkPaymentCompleted(Guid.CreateVersion7(), OrderTestFactory.UtcNow.AddMinutes(2));
        order.Confirm(OrderTestFactory.UtcNow.AddMinutes(3));
        order.MarkShipped("DHL", "TRK-42", OrderTestFactory.UtcNow.AddMinutes(4));
        order.MarkDelivered(OrderTestFactory.UtcNow.AddMinutes(5));

        // Assert
        var events = order.PopDomainEvents();
        using (new AssertionScope())
        {
            order.Status.Should().Be(OrderStatus.Delivered);
            events.Should().HaveCount(6);
            events.Select(e => e.GetType()).Should().Equal(
                typeof(OrderCreatedDomainEvent),
                typeof(OrderStockReservedDomainEvent),
                typeof(OrderPaymentCompletedDomainEvent),
                typeof(OrderConfirmedDomainEvent),
                typeof(OrderShippedDomainEvent),
                typeof(OrderDeliveredDomainEvent));
            events.OfType<DomainEvent>().Select(e => e.OccurredOnUtc).Should().BeInAscendingOrder();
        }
    }
}
