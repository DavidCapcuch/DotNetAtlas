using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using MassTransit;
using Order.AlertSubscriptions;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives AlertSubscriptionExtensionInitiatedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class AlertSubscriptionExtensionInitiatedConsumer : IConsumer<AlertSubscriptionExtensionInitiatedEvent>
{
    private readonly ILogger<AlertSubscriptionExtensionInitiatedConsumer> _logger;

    public AlertSubscriptionExtensionInitiatedConsumer(ILogger<AlertSubscriptionExtensionInitiatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AlertSubscriptionExtensionInitiatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received AlertSubscriptionExtensionInitiatedEvent for user {UserId}, " +
            "correlation {CorrelationId}, duration {DurationDays} days",
            message.UserId, message.CorrelationId, message.DurationDays);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new SubscriptionExtensionInitiatedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            DurationDays = message.DurationDays,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            IdempotencyKey = message.IdempotencyKey,
            InitiatedAtUtc = message.InitiatedAtUtc
        };

        await context.Publish(sagaEvent);
    }
}
