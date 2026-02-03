using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentFailedEvent
/// and forwards it to the SubscriptionExtensionSaga as a PaymentFailedEvent.
/// </summary>
public sealed class AlertSubscriptionExtensionPaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ILogger<AlertSubscriptionExtensionPaymentFailedConsumer> _logger;

    public AlertSubscriptionExtensionPaymentFailedConsumer(
        ILogger<AlertSubscriptionExtensionPaymentFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Extension Saga received PaymentFailedEvent for correlation {CorrelationId}, user {UserId}, error {ErrorCode}",
            message.CorrelationId, message.UserId, message.ErrorCode);

        var subscriptionExtensionPaymentFailedEvent = new AlertSubscriptionExtensionPaymentFailedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            FailedAtUtc = message.FailedAtUtc
        };

        await context.Publish(subscriptionExtensionPaymentFailedEvent);
    }
}
