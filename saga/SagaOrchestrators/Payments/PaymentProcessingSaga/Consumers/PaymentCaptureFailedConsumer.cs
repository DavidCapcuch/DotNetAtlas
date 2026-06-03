using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentCaptureFailedEvent"/> from the payment provider
/// and forwards it to the <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
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
            "{ConsumerType} received {EventType} for order {OrderId}, error {ErrorCode}: {ErrorMessage}",
            nameof(PaymentCaptureFailedConsumer), nameof(PaymentCaptureFailedEvent),
            message.OrderId, message.ErrorCode, message.ErrorMessage);

        var paymentCaptureFailedSagaEvent = new PaymentCaptureFailedSagaEvent
        {
            OrderId = message.OrderId,
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
