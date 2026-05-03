using MassTransit;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="OrderCreatedEvent"/> from <c>ordering.orders</c> and
/// forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="OrderCreatedSagaEvent"/>. Renames the Avro <c>CreatedAtUtc</c> field onto the
/// saga record's <c>OrderCreatedAtUtc</c> per docs/bc-design/checkout-saga.md § 8 row 2.
/// </summary>
public sealed class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, order {OrderId}",
            nameof(OrderCreatedConsumer), nameof(OrderCreatedEvent),
            message.CorrelationId, message.OrderId);

        await context.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = message.OrderId,
            OrderCreatedAtUtc = message.CreatedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
