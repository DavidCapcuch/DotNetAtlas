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
        var correlationId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var domainEvent = new OrderCreatedDomainEvent
        {
            OrderId = orderId,
            BuyerId = buyerId,
            CorrelationId = correlationId,
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
        avro.OrderId.Should().Be(orderId);
        avro.CorrelationId.Should().Be(correlationId);
        avro.BuyerId.Should().Be(buyerId);
        avro.PaymentMethodId.Should().Be(paymentMethodId);
        avro.Currency.Should().Be("USD");
        avro.Items.Count.Should().Be(1);
    }
}
