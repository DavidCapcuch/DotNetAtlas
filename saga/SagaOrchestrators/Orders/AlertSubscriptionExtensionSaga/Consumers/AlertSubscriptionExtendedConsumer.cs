using MassTransit;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using Weather.Alerts;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="AlertSubscriptionExtendedEvent"/> from the Weather.Alerts service
/// and forwards it to the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> as an internal
/// <see cref="AlertSubscriptionExtendedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, extended {DurationDays} days",
            nameof(AlertSubscriptionExtendedConsumer), nameof(AlertSubscriptionExtendedEvent),
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
