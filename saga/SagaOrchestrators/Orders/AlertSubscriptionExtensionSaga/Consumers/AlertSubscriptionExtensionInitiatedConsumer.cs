using MassTransit;
using Order.AlertSubscriptions;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="AlertSubscriptionExtensionInitiatedEvent"/> from Kafka
/// and forwards it to the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> as an internal
/// <see cref="AlertSubscriptionExtensionInitiatedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for user {UserId}, alertSubscriptionOrderId {AlertSubscriptionOrderId}, duration {DurationDays} days",
            nameof(AlertSubscriptionExtensionInitiatedConsumer), nameof(AlertSubscriptionExtensionInitiatedEvent),
            message.UserId, message.AlertSubscriptionOrderId, message.DurationDays);

        var subscriptionExtensionInitiatedEvent = new AlertSubscriptionExtensionInitiatedSagaEvent
        {
            CorrelationId = message.AlertSubscriptionOrderId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            DurationDays = message.DurationDays,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            InitiatedAtUtc = message.InitiatedAtUtc
        };

        await context.Publish(subscriptionExtensionInitiatedEvent);
    }
}
