using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives SubscriptionExtendedEvent
/// and forwards it to the SubscriptionExtensionSaga as internal SubscriptionExtendedEvent.
/// </summary>
public sealed class AlertSubscriptionExtendedConsumer : IConsumer<AlertSubscriptionExtendedEvent>
{
    private readonly ILogger<AlertSubscriptionExtendedConsumer> _logger;

    public AlertSubscriptionExtendedConsumer(ILogger<AlertSubscriptionExtendedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AlertSubscriptionExtendedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received Weather SubscriptionExtendedEvent for correlation {CorrelationId}, " +
            "user {UserId}, extended {DurationDays} days",
            message.CorrelationId, message.UserId, message.DurationExtendedDays);

        var subscriptionExtendedEvent = new AlertSubscriptionExtendedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            DurationExtendedDays = message.DurationExtendedDays,
            NewExpiresAtUtc = message.NewExpiresAtUtc,
            ExtendedAtUtc = message.ExtendedAtUtc
        };

        await context.Publish(subscriptionExtendedEvent);
    }
}
