using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentVoidedEvent"/> from the payment provider
/// and forwards it to the <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
/// <see cref="PaymentVoidedSagaEvent"/>.
/// </summary>
public sealed class PaymentVoidedConsumer : IConsumer<PaymentVoidedEvent>
{
    private readonly ILogger<PaymentVoidedConsumer> _logger;

    public PaymentVoidedConsumer(ILogger<PaymentVoidedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentVoidedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, authorization {AuthorizationId}",
            nameof(PaymentVoidedConsumer), nameof(PaymentVoidedEvent),
            message.OrderId, message.AuthorizationId);

        var paymentVoidedSagaEvent = new PaymentVoidedSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            AuthorizationId = message.AuthorizationId,
            VoidedAtUtc = message.VoidedAtUtc
        };

        await context.Publish(paymentVoidedSagaEvent);
    }
}
