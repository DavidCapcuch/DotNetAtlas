using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentFailedEvent
/// and forwards it to the SubscriptionExtensionSaga as a PaymentFailedEvent.
/// </summary>
public sealed class SubscriptionExtensionPaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ILogger<SubscriptionExtensionPaymentFailedConsumer> _logger;

    public SubscriptionExtensionPaymentFailedConsumer(ILogger<SubscriptionExtensionPaymentFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Extension Saga received PaymentFailedEvent for correlation {CorrelationId}, user {UserId}, error {ErrorCode}",
            message.CorrelationId, message.UserId, message.ErrorCode);

        var subscriptionExtensionPaymentFailedEvent = new SubscriptionExtensionPaymentFailedSagaEvent
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
