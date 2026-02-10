using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;

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
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, transaction {PaymentTransactionId}",
            nameof(PaymentCapturedConsumer), nameof(PaymentCapturedEvent),
            message.CorrelationId, message.PaymentTransactionId);

        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = message.CorrelationId,
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
