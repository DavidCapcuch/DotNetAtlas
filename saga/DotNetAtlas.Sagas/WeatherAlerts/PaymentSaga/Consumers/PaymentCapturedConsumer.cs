using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;
using AvroPaymentCapturedEvent = Finance.Payments.PaymentCapturedEvent;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentCapturedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class PaymentCapturedConsumer : IConsumer<AvroPaymentCapturedEvent>
{
    private readonly ILogger<PaymentCapturedConsumer> _logger;

    public PaymentCapturedConsumer(ILogger<PaymentCapturedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AvroPaymentCapturedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentCapturedEvent for correlation {CorrelationId}, transaction {PaymentTransactionId}",
            message.CorrelationId,
            message.PaymentTransactionId);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new PaymentCapturedEvent
        {
            CorrelationId = message.CorrelationId,
            PaymentTransactionId = message.PaymentTransactionId,
            CapturedAtUtc = message.CapturedAtUtc
        };

        await context.Publish(sagaEvent);
    }
}

