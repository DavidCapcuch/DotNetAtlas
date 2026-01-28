using DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentVoidedEvent
/// and forwards it to the PaymentSaga as internal PaymentVoidedSagaEvent.
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
            "Received PaymentVoidedEvent for correlation {CorrelationId}, authorization {AuthorizationId}",
            message.CorrelationId, message.AuthorizationId);

        var paymentVoidedSagaEvent = new PaymentVoidedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            AuthorizationId = message.AuthorizationId,
            VoidedAtUtc = message.VoidedAtUtc
        };

        await context.Publish(paymentVoidedSagaEvent);
    }
}
