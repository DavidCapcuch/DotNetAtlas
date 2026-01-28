using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentRefundedEvent
/// and forwards it to the SubscriptionPurchaseSaga as an internal SubscriptionCompensationCompletedEvent.
/// </summary>
public sealed class SubscriptionPurchasePaymentRefundedConsumer : IConsumer<PaymentRefundedEvent>
{
    private readonly ILogger<SubscriptionPurchasePaymentRefundedConsumer> _logger;

    public SubscriptionPurchasePaymentRefundedConsumer(ILogger<SubscriptionPurchasePaymentRefundedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRefundedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentRefundedEvent for correlation {CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            message.CorrelationId, message.UserId, message.RefundTransactionId);

        var subscriptionCompensationCompletedEvent = new SubscriptionPurchaseCompensationCompletedEvent
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
