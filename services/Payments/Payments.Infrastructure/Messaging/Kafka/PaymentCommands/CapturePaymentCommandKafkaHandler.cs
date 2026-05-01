using KafkaFlow;
using Microsoft.Extensions.Logging;
using Payments.Application.Common.Data;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppCapturePaymentCommand = Payments.Application.Transactions.CapturePayment.CapturePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Consumes the saga-issued <see cref="AvroCapturePaymentCommand"/> on
/// <c>payments.commands</c> and dispatches it to the application handler. Idempotency: inbox
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

    public Task Handle(IMessageContext context, AvroCapturePaymentCommand message) =>
        ExecuteAsync(context, message.CorrelationId, paymentId: message.CorrelationId, async ct =>
        {
            var appCommand = message.ToAppCommand();
            return await _appHandler.HandleAsync(appCommand, ct);
        });
}
