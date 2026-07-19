using Avro.Specific;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Ordering.Application.Orders.CancelOrder;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.Events;
using Ordering.Orders;
using Ordering.UnitTests.Application.Common;

namespace Ordering.UnitTests.Application.Orders.CancelOrder;

/// <summary>
/// Confirms the ADR-0020 Summary-Event contract: every
/// <see cref="OrderCancelledDomainEvent"/> emitted by Ordering carries
/// the order's Items, TotalAmount, Currency, and BillingAddress through
/// the outbox publisher onto the wire alongside the original Reason /
/// AtStatus delta payload. Invoicing's credit-note path depends on this.
/// </summary>
public class OrderCancelledOutboxPublisherDomainEventHandlerTests : HandlerTestBase
{
    [Fact]
    public async Task Handle_PublishesOrderCancelledEventToOutbox_WithOrderIdAsKey()
    {
        var order = TestAggregate.OrderAt(OrderStatus.Confirmed);
        var domainEvent = new OrderCancelledDomainEvent
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Reason = "Customer requested",
            AtStatus = OrderStatus.Confirmed.Name,
            CancelledAtUtc = TestAggregate.UtcNow,
            Items = [.. order.Items],
            Total = order.Total,
            BillingAddress = order.BillingAddress,
            OccurredOnUtc = TestAggregate.UtcNow,
        };

        var handler = new OrderCancelledOutboxPublisherDomainEventHandler(
            Outbox,
            TopicsOptions,
            NullLogger<OrderCancelledOutboxPublisherDomainEventHandler>.Instance);

        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        Outbox.Received(1).AddOutboxMessage(
            "ordering.orders",
            order.Id.ToString(),
            Arg.Any<ISpecificRecord>());
        var call = Outbox.ReceivedCalls().Single();
        var avro = (OrderCancelledEvent)call.GetArguments()[2]!;

        using (new AssertionScope())
        {
            avro.OrderId.Should().Be(order.Id);
            avro.BuyerId.Should().Be(order.BuyerId);
            avro.Reason.Should().Be("Customer requested");
            avro.AtStatus.Should().Be(OrderStatusAtTransition.Confirmed);
            avro.CancelledAtUtc.Should().Be(TestAggregate.UtcNow.UtcDateTime);

            avro.Items.Should().HaveCount(order.Items.Count);
            var firstItem = avro.Items.Single();
            var sourceItem = order.Items.Single();
            firstItem.ProductId.Should().Be(sourceItem.ProductId);
            firstItem.Sku.Should().Be(sourceItem.ProductSnapshot.Sku);
            firstItem.Name.Should().Be(sourceItem.ProductSnapshot.Name);
            firstItem.Quantity.Should().Be(sourceItem.Quantity);
            // Value via the cast, scale asserted separately: the (decimal) cast erases scale, so
            // it alone would not notice an amount emitted at the domain decimal's own scale
            // instead of the schema's 4 — which Avro rejects at encode time.
            ((decimal)firstItem.UnitPriceAmount).Should().Be(sourceItem.UnitPrice.Amount);
            firstItem.UnitPriceAmount.Scale.Should().Be(MoneyScale);
            ((decimal)firstItem.LineTotalAmount).Should().Be(sourceItem.LineTotal.Amount);
            firstItem.LineTotalAmount.Scale.Should().Be(MoneyScale);

            avro.TotalAmount.Should().NotBeNull();
            ((decimal)avro.TotalAmount!.Value).Should().Be(order.Total.Amount);
            avro.TotalAmount!.Value.Scale.Should().Be(MoneyScale);
            avro.Currency.Should().Be(order.Total.Currency.Name);

            avro.BillingAddress.Should().NotBeNull();
            avro.BillingAddress!.Street1.Should().Be(order.BillingAddress.Street1);
            avro.BillingAddress.Street2.Should().Be(order.BillingAddress.Street2);
            avro.BillingAddress.City.Should().Be(order.BillingAddress.City);
            avro.BillingAddress.State.Should().Be(order.BillingAddress.State);
            avro.BillingAddress.PostalCode.Should().Be(order.BillingAddress.PostalCode);
            avro.BillingAddress.CountryCode.Should().Be(order.BillingAddress.CountryCode);
        }
    }
}
