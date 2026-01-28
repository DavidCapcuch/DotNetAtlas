using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Events;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentRefundedEvent from the finance.payments Kafka topic
/// and forwards it to the saga as a SubscriptionCompensationCompletedEvent.
/// </summary>
public sealed class PaymentRefundedConsumer : IConsumer<PaymentRefundedEvent>
{
    private readonly ILogger<PaymentRefundedConsumer> _logger;

    public PaymentRefundedConsumer(ILogger<PaymentRefundedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRefundedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentRefundedEvent for correlation {CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            message.CorrelationId,
            message.UserId,
            message.RefundTransactionId);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new SubscriptionCompensationCompletedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            RefundTransactionId = message.RefundTransactionId,
            CompensatedAtUtc = message.RefundedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}
