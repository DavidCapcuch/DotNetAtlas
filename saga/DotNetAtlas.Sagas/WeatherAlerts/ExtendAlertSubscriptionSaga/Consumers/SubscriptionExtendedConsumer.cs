using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives SubscriptionExtendedEvent from the weather.subscriptions Kafka topic
/// and forwards it to the saga as an internal SubscriptionExtendedEvent.
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
            message.CorrelationId,
            message.UserId,
            message.DurationExtendedDays);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new Events.SubscriptionExtendedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            DurationExtendedDays = message.DurationExtendedDays,
            NewExpiresAtUtc = message.NewExpiresAtUtc,
            ExtendedAtUtc = message.ExtendedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}

