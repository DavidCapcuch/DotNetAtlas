using KafkaFlow;
using Microsoft.Extensions.Logging;
using Payments.Application.Common.Data;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppRequestRefundCommand = Payments.Application.Transactions.RequestRefund.RequestRefundCommand;
using AvroRequestRefundCommand = Payments.Transactions.RequestRefundCommand;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Consumes the saga-issued <see cref="AvroRequestRefundCommand"/> (cancel-post-capture
/// compensation) on <c>payments.payment-commands</c> and dispatches it to the application handler.
/// PaymentId comes from the wire <c>PaymentTransactionId</c>; the refund explicitly references
/// an existing transaction so the saga sends the canonical id.
/// </summary>
internal sealed class RequestRefundCommandKafkaHandler
    : SagaCommandHandlerBase<AvroRequestRefundCommand>, IMessageHandler<AvroRequestRefundCommand>
{
    private readonly ICommandHandler<AppRequestRefundCommand> _appHandler;

    public RequestRefundCommandKafkaHandler(
        ICommandHandler<AppRequestRefundCommand> appHandler,
        ITransactionalOutbox<IPaymentsDbContext> transactionalOutbox,
        ILogger<RequestRefundCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroRequestRefundCommand message)
    {
        // A refund explicitly references an existing transaction by its PaymentTransactionId, so
        // the aggregate resolves by primary key — no saga-key / Kafka-header dependency. The log
        // scope is keyed on that PaymentId.
        return ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["PaymentId"] = message.PaymentTransactionId },
            async ct =>
            {
                var appCommand = message.ToAppCommand();
                return await _appHandler.HandleAsync(appCommand, ct);
            });
    }
}
