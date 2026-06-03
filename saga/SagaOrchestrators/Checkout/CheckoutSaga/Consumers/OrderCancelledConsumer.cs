using MassTransit;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="OrderCancelledEvent"/> from <c>ordering.orders</c> and
/// forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="OrderCancelledSagaEvent"/> per docs/bc-design/checkout-saga.md § 8 row 4. Only
/// relevant during compensation; for any other saga state the orchestrator's
/// <c>OnMissingInstance(Discard)</c> + state-machine transition guards keep noise out.
/// </summary>
public sealed class OrderCancelledConsumer : IConsumer<OrderCancelledEvent>
{
    private readonly ILogger<OrderCancelledConsumer> _logger;

    public OrderCancelledConsumer(ILogger<OrderCancelledConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCancelledEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}",
            nameof(OrderCancelledConsumer), nameof(OrderCancelledEvent),
            message.OrderId);

        await context.Publish(new OrderCancelledSagaEvent
        {
            OrderId = message.OrderId,
            CancelledAtUtc = message.CancelledAtUtc.ToUtcDateTimeOffset()
        });
    }
}
