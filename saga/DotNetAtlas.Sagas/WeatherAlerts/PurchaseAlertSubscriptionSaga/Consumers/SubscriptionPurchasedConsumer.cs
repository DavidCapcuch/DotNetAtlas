using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Events;
using MassTransit;
using Order.AlertSubscriptions;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives AlertSubscriptionPurchaseInitiatedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class AlertSubscriptionPurchaseInitiatedConsumer : IConsumer<AlertSubscriptionPurchaseInitiatedEvent>
{
    private readonly ILogger<AlertSubscriptionPurchaseInitiatedConsumer> _logger;

    public AlertSubscriptionPurchaseInitiatedConsumer(ILogger<AlertSubscriptionPurchaseInitiatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AlertSubscriptionPurchaseInitiatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received AlertSubscriptionPurchaseInitiatedEvent for user {UserId}, correlation {CorrelationId}, tier {Tier}",
            message.UserId,
            message.CorrelationId,
            message.Tier);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new SubscriptionPurchaseInitiatedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            SubscriptionTier = message.Tier,
            DurationDays = message.DurationDays,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            IdempotencyKey = message.IdempotencyKey,
            InitiatedAtUtc = message.InitiatedAtUtc
        };

        await context.Publish(sagaEvent);
    }
}
