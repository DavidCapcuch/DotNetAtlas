using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentCaptureFailedEvent
/// and forwards it to the PaymentSaga as internal PaymentCaptureFailedSagaEvent.
/// </summary>
public sealed class PaymentCaptureFailedConsumer : IConsumer<PaymentCaptureFailedEvent>
{
    private readonly ILogger<PaymentCaptureFailedConsumer> _logger;

    public PaymentCaptureFailedConsumer(ILogger<PaymentCaptureFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCaptureFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Received PaymentCaptureFailedEvent for correlation {CorrelationId}, error {ErrorCode}: {ErrorMessage}",
            message.CorrelationId, message.ErrorCode, message.ErrorMessage);

        var paymentCaptureFailedSagaEvent = new PaymentCaptureFailedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            UserId = message.UserId,
            AuthorizationId = message.AuthorizationId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            IsRetryable = message.IsRetryable,
            FailedAtUtc = message.FailedAtUtc
        };

        await context.Publish(paymentCaptureFailedSagaEvent);
    }
}
