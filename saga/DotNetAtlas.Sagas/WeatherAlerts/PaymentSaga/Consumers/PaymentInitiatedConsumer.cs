using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentRequestedEvent from Kafka and forwards it to the saga.
/// This consumer acts as an adapter between the Avro-serialized Kafka message and the MassTransit saga.
/// </summary>
public sealed class PaymentRequestedConsumer : IConsumer<PaymentRequestedEvent>
{
    private readonly ILogger<PaymentRequestedConsumer> _logger;

    public PaymentRequestedConsumer(ILogger<PaymentRequestedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRequestedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received PaymentRequestedEvent for user {UserId}, correlation {CorrelationId}, amount {Amount} {Currency}",
            message.UserId,
            message.CorrelationId,
            message.Amount,
            message.Currency);

        // Convert Avro event to internal saga event and forward to the saga state machine
        var sagaEvent = new PaymentInitiatedEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            IdempotencyKey = message.IdempotencyKey,
            InitiatedAtUtc = message.RequestedAtUtc
        };

        await context.Publish(sagaEvent);
    }
}

