using MassTransit;
using Order.AlertSubscriptions;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="AlertSubscriptionPurchaseInitiatedEvent"/> from Kafka
/// and forwards it to the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/> as an internal
/// <see cref="AlertSubscriptionPurchaseInitiatedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for user {UserId}, alertSubscriptionOrderId {AlertSubscriptionOrderId}, tier {Tier}",
            nameof(AlertSubscriptionPurchaseInitiatedConsumer), nameof(AlertSubscriptionPurchaseInitiatedEvent),
            message.UserId, message.AlertSubscriptionOrderId, message.Tier);

        var subscriptionPurchaseInitiatedSagaEvent = new AlertSubscriptionPurchaseInitiatedSagaEvent
        {
            CorrelationId = message.AlertSubscriptionOrderId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            SubscriptionTier = message.Tier,
            DurationDays = message.DurationDays,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            InitiatedAtUtc = message.InitiatedAtUtc
        };

        await context.Publish(subscriptionPurchaseInitiatedSagaEvent);
    }
}
