using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;
using AvroPaymentVoidedEvent = Finance.Payments.PaymentVoidedEvent;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentVoidedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class PaymentVoidedConsumer : IConsumer<AvroPaymentVoidedEvent>
{
    private readonly ILogger<PaymentVoidedConsumer> _logger;

    public PaymentVoidedConsumer(ILogger<PaymentVoidedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AvroPaymentVoidedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentVoidedEvent for correlation {CorrelationId}, authorization {AuthorizationId}",
            message.CorrelationId,
            message.AuthorizationId);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new PaymentVoidedEvent
        {
            CorrelationId = message.CorrelationId,
            AuthorizationId = message.AuthorizationId,
            VoidedAtUtc = message.VoidedAtUtc
        };

        await context.Publish(sagaEvent);
    }
}

