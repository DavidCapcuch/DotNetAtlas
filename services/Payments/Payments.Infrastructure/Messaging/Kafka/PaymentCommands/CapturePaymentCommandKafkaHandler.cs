using KafkaFlow;
using Microsoft.Extensions.Logging;
using Payments.Application.Common.Data;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppCapturePaymentCommand = Payments.Application.Transactions.CapturePayment.CapturePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Consumes the saga-issued <see cref="AvroCapturePaymentCommand"/> on
/// <c>payments.payment-commands</c> and dispatches it to the application handler. Idempotency: inbox
/// dedup at the middleware level + terminal-status short-circuit on the aggregate.
/// </summary>
internal sealed class CapturePaymentCommandKafkaHandler
    : SagaCommandHandlerBase<AvroCapturePaymentCommand>, IMessageHandler<AvroCapturePaymentCommand>
{
    private readonly ICommandHandler<AppCapturePaymentCommand> _appHandler;

    public CapturePaymentCommandKafkaHandler(
        ICommandHandler<AppCapturePaymentCommand> appHandler,
        ITransactionalOutbox<IPaymentsDbContext> transactionalOutbox,
        ILogger<CapturePaymentCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroCapturePaymentCommand message)
    {
        // Capture carries no PaymentTransactionId on the wire — the aggregate is resolved by the
        // saga key (== OrderId per ADR-0029), carried on the Kafka header until #310 retargets the
        // read onto a wire field. The log scope is keyed on OrderId (previously mislabelled
        // PaymentId though the value was always the OrderId).
        var orderId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "Saga key (OrderId) missing on the Kafka header for CapturePaymentCommand.");

        return ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["OrderId"] = orderId },
            async ct =>
            {
                var appCommand = message.ToAppCommand(orderId);
                return await _appHandler.HandleAsync(appCommand, ct);
            });
    }
}
