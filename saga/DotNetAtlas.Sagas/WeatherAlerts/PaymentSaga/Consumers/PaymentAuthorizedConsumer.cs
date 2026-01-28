using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;
using AvroPaymentAuthorizedEvent = Finance.Payments.PaymentAuthorizedEvent;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentAuthorizedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class PaymentAuthorizedConsumer : IConsumer<AvroPaymentAuthorizedEvent>
{
    private readonly ILogger<PaymentAuthorizedConsumer> _logger;

    public PaymentAuthorizedConsumer(ILogger<PaymentAuthorizedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AvroPaymentAuthorizedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentAuthorizedEvent for correlation {CorrelationId}, authorization {AuthorizationId}",
            message.CorrelationId,
            message.AuthorizationId);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new PaymentAuthorizedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            AuthorizationId = message.AuthorizationId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            AuthorizedAtUtc = message.AuthorizedAtUtc,
            ExpiresAtUtc = message.ExpiresAtUtc
        };

        await context.Publish(sagaEvent);
    }
}

