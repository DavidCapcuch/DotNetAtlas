using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentFailedEvent from the Finance.Payments Kafka topic
/// and forwards it to the Extension saga as an internal PaymentFailedEvent.
/// </summary>
public sealed class PaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ILogger<PaymentFailedConsumer> _logger;

    public PaymentFailedConsumer(ILogger<PaymentFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Extension Saga received PaymentFailedEvent for correlation {CorrelationId}, user {UserId}, error {ErrorCode}",
            message.CorrelationId,
            message.UserId,
            message.ErrorCode);

        // Adapt the Avro event to the saga's internal event
        var sagaEvent = new Events.PaymentFailedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            FailedAtUtc = message.FailedAtUtc
        };

        // Forward to the saga state machine via the in-memory bus
        await context.Publish(sagaEvent);
    }
}

