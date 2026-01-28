using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives SubscriptionActivatedEvent
/// and forwards it to the SubscriptionPurchaseSaga as internal SubscriptionActivatedEvent.
/// </summary>
public sealed class SubscriptionActivatedConsumer : IConsumer<SubscriptionActivatedEvent>
{
    private readonly ILogger<SubscriptionActivatedConsumer> _logger;

    public SubscriptionActivatedConsumer(ILogger<SubscriptionActivatedConsumer> logger)
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

        var subscriptionActivatedEvent = new InternalSagaEvents.SubscriptionActivatedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            ActivatedAtUtc = message.ActivatedAtUtc
        };

        await context.Publish(subscriptionActivatedEvent);
    }
}
