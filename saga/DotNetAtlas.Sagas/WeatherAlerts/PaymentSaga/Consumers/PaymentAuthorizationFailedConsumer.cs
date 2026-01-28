using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;
using AvroPaymentAuthorizationFailedEvent = Finance.Payments.PaymentAuthorizationFailedEvent;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentAuthorizationFailedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class PaymentAuthorizationFailedConsumer : IConsumer<AvroPaymentAuthorizationFailedEvent>
{
    private readonly ILogger<PaymentAuthorizationFailedConsumer> _logger;

    public PaymentAuthorizationFailedConsumer(ILogger<PaymentAuthorizationFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AvroPaymentAuthorizationFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Received PaymentAuthorizationFailedEvent for correlation {CorrelationId}, error {ErrorCode}: {ErrorMessage}",
            message.CorrelationId,
            message.ErrorCode,
            message.ErrorMessage);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new PaymentAuthorizationFailedEvent
        {
            CorrelationId = message.CorrelationId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            IsRetryable = message.IsRetryable,
            FailedAtUtc = message.FailedAtUtc
        };

        await context.Publish(sagaEvent);
    }
}

