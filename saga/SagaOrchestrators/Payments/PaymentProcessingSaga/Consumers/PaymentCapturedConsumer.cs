using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentCapturedEvent"/> from the payment provider
/// and forwards it to the <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
/// <see cref="PaymentCapturedSagaEvent"/>.
/// </summary>
public sealed class PaymentCapturedConsumer : IConsumer<PaymentCapturedEvent>
{
    private readonly ILogger<PaymentCapturedConsumer> _logger;

    public PaymentCapturedConsumer(ILogger<PaymentCapturedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCapturedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, transaction {PaymentTransactionId}",
            nameof(PaymentCapturedConsumer), nameof(PaymentCapturedEvent),
            message.OrderId, message.PaymentTransactionId);

        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            PaymentTransactionId = message.PaymentTransactionId,
            AuthorizationId = message.AuthorizationId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            CapturedAtUtc = message.CapturedAtUtc
        };

        await context.Publish(paymentCapturedSagaEvent);
    }
}
