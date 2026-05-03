using MassTransit;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="OrderFailedEvent"/> from <c>ordering.orders</c> and
/// forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="OrderFailedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8 row 5. Drives
/// transitions to terminal <c>Failed</c> (from <c>AwaitingOrderCreation</c>) or to
/// <c>CompensatingPayment</c> (from <c>AwaitingConfirmation</c>) per § 4 transition table.
/// </summary>
public sealed class OrderFailedConsumer : IConsumer<OrderFailedEvent>
{
    private readonly ILogger<OrderFailedConsumer> _logger;

    public OrderFailedConsumer(ILogger<OrderFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, order {OrderId}, error {ErrorCode}",
            nameof(OrderFailedConsumer), nameof(OrderFailedEvent),
            message.CorrelationId, message.OrderId, message.ErrorCode);

        await context.Publish(new OrderFailedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = message.OrderId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            FailedAtUtc = message.FailedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
