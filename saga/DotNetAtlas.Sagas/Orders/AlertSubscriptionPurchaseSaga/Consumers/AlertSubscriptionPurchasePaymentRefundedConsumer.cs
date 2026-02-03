using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentRefundedEvent
/// and forwards it to the SubscriptionPurchaseSaga as an internal SubscriptionCompensationCompletedEvent.
/// </summary>
public sealed class AlertSubscriptionPurchasePaymentRefundedConsumer : IConsumer<PaymentRefundedEvent>
{
    private readonly ILogger<AlertSubscriptionPurchasePaymentRefundedConsumer> _logger;

    public AlertSubscriptionPurchasePaymentRefundedConsumer(ILogger<AlertSubscriptionPurchasePaymentRefundedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRefundedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentRefundedEvent for correlation {CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            message.CorrelationId, message.UserId, message.RefundTransactionId);

        var subscriptionCompensationCompletedEvent = new AlertSubscriptionPurchaseCompensationCompletedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            RefundTransactionId = message.RefundTransactionId,
            CompensatedAtUtc = message.RefundedAtUtc
        };

        await context.Publish(subscriptionCompensationCompletedEvent);
    }
}
