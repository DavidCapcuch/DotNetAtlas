using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentFailedEvent from the Finance.Payments Kafka topic
/// and forwards it to the saga as an internal PaymentFailedEvent.
/// </summary>
public sealed class AlertSubscriptionPurchasePaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ILogger<AlertSubscriptionPurchasePaymentFailedConsumer> _logger;

    public AlertSubscriptionPurchasePaymentFailedConsumer(ILogger<AlertSubscriptionPurchasePaymentFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentFailedEvent for correlation {CorrelationId}, user {UserId}: {ErrorCode} - {ErrorMessage}",
            message.CorrelationId, message.UserId, message.ErrorCode, message.ErrorMessage);

        var subscriptionPurchasePaymentFailedSagaEvent = new AlertSubscriptionPurchasePaymentFailedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            FailedAtUtc = message.FailedAtUtc
        };

        await context.Publish(subscriptionPurchasePaymentFailedSagaEvent);
    }
}
