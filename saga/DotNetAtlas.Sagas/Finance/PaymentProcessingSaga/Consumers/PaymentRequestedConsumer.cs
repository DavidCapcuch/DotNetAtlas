using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentRequestedEvent"/> from Kafka
/// and forwards it to the <see cref="PaymentProcessingSaga"/> as an internal
/// <see cref="PaymentInitiatedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for user {UserId}, correlation {CorrelationId}, amount {Amount} {Currency}",
            nameof(PaymentRequestedConsumer), nameof(PaymentRequestedEvent),
            message.UserId, message.CorrelationId, message.Amount, message.Currency);

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            IdempotencyKey = message.IdempotencyKey,
            InitiatedAtUtc = message.RequestedAtUtc
        };

        await context.Publish(paymentInitiatedSagaEvent);
    }
}
