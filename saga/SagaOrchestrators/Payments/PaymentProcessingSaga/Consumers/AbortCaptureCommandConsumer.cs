using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="AbortCaptureCommand"/> from <c>payments.payment-commands</c>
/// (sent by the Checkout saga when its confirmation step fails) and forwards it to the
/// <see cref="PaymentProcessingSagaOrchestrator"/> as an internal <see cref="AbortCaptureSagaEvent"/>
/// (ADR-0026). Drives the sub-saga's pre-capture void path.
/// </summary>
public sealed class AbortCaptureCommandConsumer : IConsumer<AbortCaptureCommand>
{
    private readonly ILogger<AbortCaptureCommandConsumer> _logger;

    public AbortCaptureCommandConsumer(ILogger<AbortCaptureCommandConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AbortCaptureCommand> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for user {UserId}, order {OrderId}, reason {Reason}",
            nameof(AbortCaptureCommandConsumer), nameof(AbortCaptureCommand),
            message.UserId, message.OrderId, message.Reason);

        var abortCaptureSagaEvent = new AbortCaptureSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            Reason = message.Reason,
            RequestedAtUtc = message.RequestedAtUtc
        };

        await context.Publish(abortCaptureSagaEvent);
    }
}
