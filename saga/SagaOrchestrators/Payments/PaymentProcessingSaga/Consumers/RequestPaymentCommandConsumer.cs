using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="RequestPaymentCommand"/> from
/// <c>payments.payment-commands</c> and forwards it to the
/// <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
/// <see cref="PaymentInitiatedSagaEvent"/>. Renamed from <c>PaymentRequestedConsumer</c> per
/// ADR-0023; the imperative-to-declarative translation at this boundary is intentional —
/// the wire is a command (the Checkout saga awaits guaranteed feedback) while the FSM
/// transition trigger reads naturally as "the payment-processing saga has been initiated".
/// </summary>
public sealed class RequestPaymentCommandConsumer : IConsumer<RequestPaymentCommand>
{
    private readonly ILogger<RequestPaymentCommandConsumer> _logger;

    public RequestPaymentCommandConsumer(ILogger<RequestPaymentCommandConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RequestPaymentCommand> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for user {UserId}, order {OrderId}, amount {Amount} {Currency}",
            nameof(RequestPaymentCommandConsumer), nameof(RequestPaymentCommand),
            message.UserId, message.OrderId, message.Amount, message.Currency);

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            PaymentMethodId = message.PaymentMethodId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            IdempotencyKey = message.IdempotencyKey,
            InitiatedAtUtc = message.RequestedAtUtc
        };

        await context.Publish(paymentInitiatedSagaEvent);
    }
}
