using KafkaFlow;
using Microsoft.Extensions.Logging;
using Payments.Application.Common.Data;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppVoidPaymentCommand = Payments.Application.Transactions.VoidPayment.VoidPaymentCommand;
using AvroVoidPaymentCommand = Payments.Transactions.VoidPaymentCommand;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Consumes the saga-issued <see cref="AvroVoidPaymentCommand"/> (pre-capture compensation) on
/// <c>payments.payment-commands</c> and dispatches it to the application handler.
/// </summary>
internal sealed class VoidPaymentCommandKafkaHandler
    : SagaCommandHandlerBase<AvroVoidPaymentCommand>, IMessageHandler<AvroVoidPaymentCommand>
{
    private readonly ICommandHandler<AppVoidPaymentCommand> _appHandler;

    public VoidPaymentCommandKafkaHandler(
        ICommandHandler<AppVoidPaymentCommand> appHandler,
        ITransactionalOutbox<IPaymentsDbContext> transactionalOutbox,
        ILogger<VoidPaymentCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroVoidPaymentCommand message)
    {
        // Void carries no PaymentTransactionId on the wire — the aggregate is resolved by the
        // wire OrderId (the saga key == OrderId per ADR-0029), which also keys the log scope.
        return ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["OrderId"] = message.OrderId },
            async ct =>
            {
                var appCommand = message.ToAppCommand();
                return await _appHandler.HandleAsync(appCommand, ct);
            });
    }
}
