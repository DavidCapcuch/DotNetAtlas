using MassTransit;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="OrderConfirmedEvent"/> from <c>ordering.orders</c> and
/// forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="OrderConfirmedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8 row 3.
/// Consumed in <c>AwaitingConfirmation</c> as the gating event for terminal <c>Confirmed</c>.
/// </summary>
public sealed class OrderConfirmedConsumer : IConsumer<OrderConfirmedEvent>
{
    private readonly ILogger<OrderConfirmedConsumer> _logger;

    public OrderConfirmedConsumer(ILogger<OrderConfirmedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderConfirmedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}",
            nameof(OrderConfirmedConsumer), nameof(OrderConfirmedEvent),
            message.OrderId);

        await context.Publish(new OrderConfirmedSagaEvent
        {
            OrderId = message.OrderId,
            ConfirmedAtUtc = message.ConfirmedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
