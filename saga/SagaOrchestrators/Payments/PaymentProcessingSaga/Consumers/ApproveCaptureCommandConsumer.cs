using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="ApproveCaptureCommand"/> from <c>payments.payment-commands</c>
/// (sent by the Checkout saga once stock + order are confirmed) and forwards it to the
/// <see cref="PaymentProcessingSagaOrchestrator"/> as an internal <see cref="ApproveCaptureSagaEvent"/>
/// (ADR-0026). The imperative-to-declarative translation at this boundary mirrors
/// <see cref="RequestPaymentCommandConsumer"/>.
/// </summary>
public sealed class ApproveCaptureCommandConsumer : IConsumer<ApproveCaptureCommand>
{
    private readonly ILogger<ApproveCaptureCommandConsumer> _logger;

    public ApproveCaptureCommandConsumer(ILogger<ApproveCaptureCommandConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ApproveCaptureCommand> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for user {UserId}, correlation {CorrelationId}",
            nameof(ApproveCaptureCommandConsumer), nameof(ApproveCaptureCommand),
            message.UserId, message.CorrelationId);

        var approveCaptureSagaEvent = new ApproveCaptureSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            RequestedAtUtc = message.RequestedAtUtc
        };

        await context.Publish(approveCaptureSagaEvent);
    }
}
