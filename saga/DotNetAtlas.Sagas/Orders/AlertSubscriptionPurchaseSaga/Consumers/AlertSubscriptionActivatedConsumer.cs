using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="AlertSubscriptionActivatedEvent"/> from the Weather.Alerts service
/// and forwards it to the <see cref="AlertSubscriptionPurchaseSaga"/> as an internal
/// <see cref="AlertSubscriptionActivatedSagaEvent"/>.
/// </summary>
public sealed class AlertSubscriptionActivatedConsumer : IConsumer<AlertSubscriptionActivatedEvent>
{
    private readonly ILogger<AlertSubscriptionActivatedConsumer> _logger;

    public AlertSubscriptionActivatedConsumer(ILogger<AlertSubscriptionActivatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AlertSubscriptionActivatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}",
            nameof(AlertSubscriptionActivatedConsumer), nameof(AlertSubscriptionActivatedEvent),
            message.CorrelationId, message.UserId);

        var subscriptionActivatedEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            ActivatedAtUtc = message.ActivatedAtUtc
        };

        await context.Publish(subscriptionActivatedEvent);
    }
}
