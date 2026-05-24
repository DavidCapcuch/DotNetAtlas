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
/// <c>payments.commands</c> and dispatches it to the application handler. Idempotency is
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
        // ADR-0008 — Kafka header is the authoritative CorrelationId; Avro payload field is
        // convenience metadata only. PaymentId derives from it per the one-payment-per-saga rule.
        var correlationId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "CorrelationId header missing on Kafka message — ConsumerCorrelationIdMiddleware should have populated it.");

        return ExecuteAsync(context, correlationId, paymentId: correlationId, async ct =>
        {
            var appCommand = message.ToAppCommand(correlationId);
            var result = await _appHandler.HandleAsync(appCommand, ct);
            return result.ToResult();
        });
    }
}
