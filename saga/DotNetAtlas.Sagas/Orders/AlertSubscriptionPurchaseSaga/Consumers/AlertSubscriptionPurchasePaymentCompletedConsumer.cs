using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentCompletedEvent from the Finance.Payments Kafka topic
/// and forwards it to the saga as an internal PaymentCompletedEvent.
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
            "Received PaymentCompletedEvent for correlation {CorrelationId}, user {UserId}, transaction {PaymentTransactionId}",
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
