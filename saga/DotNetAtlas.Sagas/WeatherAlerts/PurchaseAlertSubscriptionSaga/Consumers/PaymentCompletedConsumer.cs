using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentCompletedEvent from the Finance.Payments Kafka topic
/// and forwards it to the saga as an internal PaymentCompletedEvent.
/// </summary>
public sealed class PaymentCompletedConsumer : IConsumer<PaymentCompletedEvent>
{
    private readonly ILogger<PaymentCompletedConsumer> _logger;

    public PaymentCompletedConsumer(ILogger<PaymentCompletedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentCompletedEvent for correlation {CorrelationId}, user {UserId}, transaction {PaymentTransactionId}",
            message.CorrelationId,
            message.UserId,
            message.PaymentTransactionId);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new Events.PaymentCompletedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            CompletedAtUtc = message.CompletedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}

