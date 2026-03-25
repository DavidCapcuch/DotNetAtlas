using Finance.Payments;
using MassTransit;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentRefundedEvent"/> from the payment service
/// and forwards it to the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/> as an internal
/// <see cref="AlertSubscriptionPurchaseCompensationCompletedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            nameof(AlertSubscriptionPurchasePaymentRefundedConsumer), nameof(PaymentRefundedEvent),
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
