using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentCompletedEvent"/> from the payment service
/// and forwards it to the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> as an
/// <see cref="AlertSubscriptionExtensionPaymentCompletedSagaEvent"/>.
/// </summary>
public sealed class AlertSubscriptionExtensionPaymentCompletedConsumer : IConsumer<PaymentCompletedEvent>
{
    private readonly ILogger<AlertSubscriptionExtensionPaymentCompletedConsumer> _logger;

    public AlertSubscriptionExtensionPaymentCompletedConsumer(
        ILogger<AlertSubscriptionExtensionPaymentCompletedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, transaction {PaymentTransactionId}",
            nameof(AlertSubscriptionExtensionPaymentCompletedConsumer), nameof(PaymentCompletedEvent),
            message.CorrelationId, message.UserId, message.PaymentTransactionId);

        var subscriptionExtensionPaymentCompletedEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            CompletedAtUtc = message.CompletedAtUtc
        };

        await context.Publish(subscriptionExtensionPaymentCompletedEvent);
    }
}
