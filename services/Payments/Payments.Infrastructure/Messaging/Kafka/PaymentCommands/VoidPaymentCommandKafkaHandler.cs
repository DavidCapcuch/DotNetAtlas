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
        // Void carries no PaymentTransactionId on the wire — the aggregate is resolved by the saga
        // key (== OrderId per ADR-0029), carried on the Kafka header until #310 retargets the read
        // onto a wire field. The log scope is keyed on OrderId (previously mislabelled PaymentId
        // though the value was always the OrderId).
        var orderId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "Saga key (OrderId) missing on the Kafka header for VoidPaymentCommand.");

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
