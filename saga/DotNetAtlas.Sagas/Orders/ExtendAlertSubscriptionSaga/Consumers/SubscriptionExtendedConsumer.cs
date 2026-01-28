using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives SubscriptionExtendedEvent
/// and forwards it to the SubscriptionExtensionSaga as internal SubscriptionExtendedEvent.
/// </summary>
public sealed class SubscriptionExtendedConsumer : IConsumer<SubscriptionExtendedEvent>
{
    private readonly ILogger<SubscriptionExtendedConsumer> _logger;

    public SubscriptionExtendedConsumer(ILogger<SubscriptionExtendedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SubscriptionExtendedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received Weather SubscriptionExtendedEvent for correlation {CorrelationId}, user {UserId}, extended {DurationDays} days",
            message.CorrelationId, message.UserId, message.DurationExtendedDays);

        var subscriptionExtendedEvent = new SubscriptionExtendedSagaEvent
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
