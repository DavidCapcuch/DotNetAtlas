using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentAuthorizationFailedEvent
/// and forwards it to the PaymentSaga as internal PaymentAuthorizationFailedSagaEvent.
/// </summary>
public sealed class PaymentAuthorizationFailedConsumer : IConsumer<PaymentAuthorizationFailedEvent>
{
    private readonly ILogger<PaymentAuthorizationFailedConsumer> _logger;

    public PaymentAuthorizationFailedConsumer(ILogger<PaymentAuthorizationFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentAuthorizationFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Received PaymentAuthorizationFailedEvent for correlation {CorrelationId}, error {ErrorCode}: {ErrorMessage}",
            message.CorrelationId, message.ErrorCode, message.ErrorMessage);

        var paymentAuthorizationFailedSagaEvent = new PaymentAuthorizationFailedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            IsRetryable = message.IsRetryable,
            FailedAtUtc = message.FailedAtUtc
        };

        await context.Publish(paymentAuthorizationFailedSagaEvent);
    }
}
