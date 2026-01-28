using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives SubscriptionActivatedEvent from the weather.subscriptions Kafka topic
/// and forwards it to the saga as an internal SubscriptionActivatedEvent.
/// </summary>
public sealed class WeatherSubscriptionActivatedConsumer : IConsumer<SubscriptionActivatedEvent>
{
    private readonly ILogger<WeatherSubscriptionActivatedConsumer> _logger;

    public WeatherSubscriptionActivatedConsumer(ILogger<WeatherSubscriptionActivatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SubscriptionActivatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received Weather SubscriptionActivatedEvent for correlation {CorrelationId}, user {UserId}",
            message.CorrelationId,
            message.UserId);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new Events.SubscriptionActivatedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            ActivatedAtUtc = message.ActivatedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}
