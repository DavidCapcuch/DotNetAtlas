using Finance.Payments;
using MassTransit;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentCompletedEvent"/> from the Finance.Payments Kafka topic
/// and forwards it to the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/> as an internal
/// <see cref="AlertSubscriptionPurchasePaymentCompletedSagaEvent"/>.
/// </summary>
public sealed class AlertSubscriptionPurchasePaymentCompletedConsumer : IConsumer<PaymentCompletedEvent>
{
    private readonly ILogger<AlertSubscriptionPurchasePaymentCompletedConsumer> _logger;

    public AlertSubscriptionPurchasePaymentCompletedConsumer(ILogger<AlertSubscriptionPurchasePaymentCompletedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, transaction {PaymentTransactionId}",
            nameof(AlertSubscriptionPurchasePaymentCompletedConsumer), nameof(PaymentCompletedEvent),
            message.CorrelationId, message.UserId, message.PaymentTransactionId);

        var subscriptionPurchasePaymentCompletedSagaEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            CompletedAtUtc = message.CompletedAtUtc
        };

        await context.Publish(subscriptionPurchasePaymentCompletedSagaEvent);
    }
}
