using Avro;
using Avro.Specific;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Ordering.Application.Orders.CreateOrder;
using Ordering.Domain.Orders.Events;
using Ordering.Orders;
using Ordering.UnitTests.Application.Common;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.UnitTests.Application.Orders.CreateOrder;

public class OrderCreatedOutboxPublisherDomainEventHandlerTests : HandlerTestBase
{
    [Fact]
    public async Task Handle_PublishesOrderCreatedEventToOutbox_WithOrderIdAsKey()
    {
        var orderId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var domainEvent = new OrderCreatedDomainEvent
        {
            OrderId = orderId,
            BuyerId = buyerId,
            PaymentMethodId = paymentMethodId,
            Items = [new OrderCreatedDomainEventItem(
                Guid.CreateVersion7(), "SKU", "Prod", 2, 10m, 20m)],
            ShippingAddress = TestAggregate.ShippingAddress(),
            BillingAddress = TestAggregate.BillingAddress(),
            Total = Money.Create(20m, CurrencyCode.Usd).Value,
            CreatedAtUtc = TestAggregate.UtcNow,
            OccurredOnUtc = TestAggregate.UtcNow,
        };

        var handler = new OrderCreatedOutboxPublisherDomainEventHandler(
            Outbox,
            TopicsOptions,
            NullLogger<OrderCreatedOutboxPublisherDomainEventHandler>.Instance);

        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        Outbox.Received(1).AddOutboxMessage(
            "ordering.orders",
            orderId.ToString(),
            Arg.Any<ISpecificRecord>());
        var call = Outbox.ReceivedCalls().Single();
        var avro = (OrderCreatedEvent)call.GetArguments()[2]!;

        using (new AssertionScope())
        {
            avro.OrderId.Should().Be(orderId);
            avro.BuyerId.Should().Be(buyerId);
            avro.PaymentMethodId.Should().Be(paymentMethodId);
            avro.Currency.Should().Be("USD");
            avro.Items.Count.Should().Be(1);

            // Money was previously unasserted here, so a wrong amount or a wrong scale shipped
            // silently. Scale-comparing oracle: AvroDecimal equality covers Scale as well as the
            // unscaled value, and the schema pins every money field at 4.
            avro.TotalAmount.Should().Be(new AvroDecimal(20.0000m));
            avro.TotalAmount.Scale.Should().Be(MoneyScale);

            var item = avro.Items[0];
            item.UnitPriceAmount.Should().Be(new AvroDecimal(10.0000m));
            item.UnitPriceAmount.Scale.Should().Be(MoneyScale);
            item.LineTotalAmount.Should().Be(new AvroDecimal(20.0000m));
            item.LineTotalAmount.Scale.Should().Be(MoneyScale);
        }
    }
}
