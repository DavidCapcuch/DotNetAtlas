using Finance.Payments;
using MassTransit;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentRefundedEvent"/> from the payment service
/// and forwards it to the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> as an
/// <see cref="AlertSubscriptionExtensionCompensationCompletedSagaEvent"/>.
/// </summary>
public sealed class AlertSubscriptionExtensionPaymentRefundedConsumer : IConsumer<PaymentRefundedEvent>
{
    private readonly ILogger<AlertSubscriptionExtensionPaymentRefundedConsumer> _logger;

    public AlertSubscriptionExtensionPaymentRefundedConsumer(
        ILogger<AlertSubscriptionExtensionPaymentRefundedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRefundedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            nameof(AlertSubscriptionExtensionPaymentRefundedConsumer), nameof(PaymentRefundedEvent),
            message.CorrelationId, message.UserId, message.RefundTransactionId);

        var alertSubscriptionExtensionCompensationCompletedSagaEvent =
            new AlertSubscriptionExtensionCompensationCompletedSagaEvent
            {
                CorrelationId = message.CorrelationId,
                UserId = message.UserId,
                RefundTransactionId = message.RefundTransactionId,
                CompensatedAtUtc = message.RefundedAtUtc
            };

        await context.Publish(alertSubscriptionExtensionCompensationCompletedSagaEvent);
    }
}
