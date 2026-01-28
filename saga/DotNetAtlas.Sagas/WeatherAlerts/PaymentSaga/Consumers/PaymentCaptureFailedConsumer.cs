using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;
using AvroPaymentCaptureFailedEvent = Finance.Payments.PaymentCaptureFailedEvent;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentCaptureFailedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class PaymentCaptureFailedConsumer : IConsumer<AvroPaymentCaptureFailedEvent>
{
    private readonly ILogger<PaymentCaptureFailedConsumer> _logger;

    public PaymentCaptureFailedConsumer(ILogger<PaymentCaptureFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AvroPaymentCaptureFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Received PaymentCaptureFailedEvent for correlation {CorrelationId}, error {ErrorCode}: {ErrorMessage}",
            message.CorrelationId,
            message.ErrorCode,
            message.ErrorMessage);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new PaymentCaptureFailedEvent
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

