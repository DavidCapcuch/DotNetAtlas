using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentAuthorizationFailedEvent"/> from the payment provider
/// and forwards it to the <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
/// <see cref="PaymentAuthorizationFailedSagaEvent"/>.
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
            "{ConsumerType} received {EventType} for order {OrderId}, error {ErrorCode}: {ErrorMessage}",
            nameof(PaymentAuthorizationFailedConsumer), nameof(PaymentAuthorizationFailedEvent),
            message.OrderId, message.ErrorCode, message.ErrorMessage);

        var paymentAuthorizationFailedSagaEvent = new PaymentAuthorizationFailedSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            IsRetryable = message.IsRetryable,
            FailedAtUtc = message.FailedAtUtc
        };

        await context.Publish(paymentAuthorizationFailedSagaEvent);
    }
}
