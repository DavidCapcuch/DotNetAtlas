using FluentResults;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Payments.Application.Common.Data;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppAuthorizePaymentCommand = Payments.Application.Transactions.AuthorizePayment.AuthorizePaymentCommand;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Consumes the saga-issued <see cref="AvroAuthorizePaymentCommand"/> on
/// <c>payments.payment-commands</c> and dispatches it to the application handler. Idempotency is
/// enforced by KafkaFlow inbox middleware (message-id dedup) plus the handler's
/// terminal-status short-circuit.
/// </summary>
internal sealed class AuthorizePaymentCommandKafkaHandler
    : SagaCommandHandlerBase<AvroAuthorizePaymentCommand>, IMessageHandler<AvroAuthorizePaymentCommand>
{
    private readonly ICommandHandler<AppAuthorizePaymentCommand, Guid> _appHandler;

    public AuthorizePaymentCommandKafkaHandler(
        ICommandHandler<AppAuthorizePaymentCommand, Guid> appHandler,
        ITransactionalOutbox<IPaymentsDbContext> transactionalOutbox,
        ILogger<AuthorizePaymentCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroAuthorizePaymentCommand message)
    {
        // The aggregate is keyed on the wire OrderId (== saga key per ADR-0029); the aggregate PK
        // is the saga-issued PaymentTransactionId (#255). The log scope uses both wire ids.
        return ExecuteAsync(
            context,
            new Dictionary<string, object?>
            {
                ["OrderId"] = message.OrderId,
                ["PaymentId"] = message.PaymentTransactionId,
            },
            async ct =>
            {
                var appCommand = message.ToAppCommand();
                var result = await _appHandler.HandleAsync(appCommand, ct);
                return result.ToResult();
            });
    }
}
