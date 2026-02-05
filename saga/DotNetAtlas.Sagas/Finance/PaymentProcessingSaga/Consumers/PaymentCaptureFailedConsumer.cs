using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentCaptureFailedEvent"/> from the payment provider
/// and forwards it to the <see cref="PaymentProcessingSaga"/> as an internal
/// <see cref="PaymentCaptureFailedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, error {ErrorCode}: {ErrorMessage}",
            nameof(PaymentCaptureFailedConsumer), nameof(PaymentCaptureFailedEvent),
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
