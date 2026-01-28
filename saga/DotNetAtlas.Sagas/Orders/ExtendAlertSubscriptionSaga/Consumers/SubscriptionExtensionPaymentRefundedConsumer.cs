using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentRefundedEvent
/// and forwards it to the SubscriptionExtensionSaga as an ExtensionCompensationCompletedEvent.
/// </summary>
public sealed class SubscriptionExtensionPaymentRefundedConsumer : IConsumer<PaymentRefundedEvent>
{
    private readonly ILogger<SubscriptionExtensionPaymentRefundedConsumer> _logger;

    public SubscriptionExtensionPaymentRefundedConsumer(ILogger<SubscriptionExtensionPaymentRefundedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRefundedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Extension Saga received PaymentRefundedEvent for correlation " +
            "{CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            message.CorrelationId, message.UserId, message.RefundTransactionId);

        var extensionCompensationCompletedEvent = new ExtensionCompensationCompletedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            RefundTransactionId = message.RefundTransactionId,
            CompensatedAtUtc = message.RefundedAtUtc
        };

        await context.Publish(extensionCompensationCompletedEvent);
    }
}
