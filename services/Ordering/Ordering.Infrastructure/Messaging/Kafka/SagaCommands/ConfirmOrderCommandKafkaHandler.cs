using KafkaFlow;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppConfirmOrderCommand = Ordering.Application.Orders.ConfirmOrder.ConfirmOrderCommand;
using AvroConfirmOrderCommand = Ordering.Orders.ConfirmOrderCommand;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>ConfirmOrderCommand</c> on
/// <c>ordering.order-commands</c> and dispatches it to the application
/// handler. Idempotency is enforced by KafkaFlow inbox middleware.
/// </summary>
internal sealed class ConfirmOrderCommandKafkaHandler
    : SagaCommandHandlerBase<AvroConfirmOrderCommand>, IMessageHandler<AvroConfirmOrderCommand>
{
    private readonly ICommandHandler<AppConfirmOrderCommand> _appHandler;

    public ConfirmOrderCommandKafkaHandler(
        ICommandHandler<AppConfirmOrderCommand> appHandler,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        ILogger<ConfirmOrderCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroConfirmOrderCommand message)
    {
        // ADR-0008 — Kafka header is the authoritative CorrelationId source.
        var correlationId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "CorrelationId header missing on Kafka message — ConsumerCorrelationIdMiddleware should have populated it.");

        return ExecuteAsync(context, correlationId, message.OrderId,
            ct => _appHandler.HandleAsync(message.ToAppCommand(), ct));
    }
}
