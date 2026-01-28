using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentRefundedEvent from the finance.payments Kafka topic
/// and forwards it to the Extension saga as an ExtensionCompensationCompletedEvent.
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
            "Extension Saga received PaymentRefundedEvent for correlation {CorrelationId}, user {UserId}, refund transaction {RefundTransactionId}",
            message.CorrelationId,
            message.UserId,
            message.RefundTransactionId);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new ExtensionCompensationCompletedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            RefundTransactionId = message.RefundTransactionId,
            CompensatedAtUtc = message.RefundedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}

