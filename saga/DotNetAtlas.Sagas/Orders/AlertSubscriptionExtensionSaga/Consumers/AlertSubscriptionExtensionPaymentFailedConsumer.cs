using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentFailedEvent"/> from the payment service
/// and forwards it to the <see cref="AlertSubscriptionExtensionSaga"/> as an
/// <see cref="AlertSubscriptionExtensionPaymentFailedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, error {ErrorCode}",
            nameof(AlertSubscriptionExtensionPaymentFailedConsumer), nameof(PaymentFailedEvent),
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
