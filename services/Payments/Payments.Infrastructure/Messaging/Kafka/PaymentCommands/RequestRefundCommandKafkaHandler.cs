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
/// compensation) on <c>payments.commands</c> and dispatches it to the application handler.
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

    public Task Handle(IMessageContext context, AvroRequestRefundCommand message) =>
        ExecuteAsync(context, message.CorrelationId, paymentId: message.PaymentTransactionId, async ct =>
        {
            var appCommand = message.ToAppCommand();
            return await _appHandler.HandleAsync(appCommand, ct);
        });
}
