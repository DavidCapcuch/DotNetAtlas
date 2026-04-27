using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentRequestedEvent"/> from Kafka
/// and forwards it to the <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
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
            "{ConsumerType} received {EventType} for user {UserId}, order {OrderId}, correlation {CorrelationId}, amount {Amount} {Currency}",
            nameof(PaymentRequestedConsumer), nameof(PaymentRequestedEvent),
            message.UserId, message.OrderId, message.CorrelationId, message.Amount, message.Currency);

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            OrderId = message.OrderId,
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
