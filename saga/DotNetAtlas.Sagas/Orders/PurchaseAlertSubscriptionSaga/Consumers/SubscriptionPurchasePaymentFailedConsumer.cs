using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentFailedEvent from the Finance.Payments Kafka topic
/// and forwards it to the saga as an internal PaymentFailedEvent.
/// </summary>
public sealed class SubscriptionPurchasePaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ILogger<SubscriptionPurchasePaymentFailedConsumer> _logger;

    public SubscriptionPurchasePaymentFailedConsumer(ILogger<SubscriptionPurchasePaymentFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentFailedEvent for correlation {CorrelationId}, user {UserId}: {ErrorCode} - {ErrorMessage}",
            message.CorrelationId,
            message.UserId,
            message.ErrorCode,
            message.ErrorMessage);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new InternalSagaEvents.SubscriptionPurchasePaymentFailedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            FailedAtUtc = message.FailedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}
