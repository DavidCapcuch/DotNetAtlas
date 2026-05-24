using KafkaFlow;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppMarkOrderFailedCommand = Ordering.Application.Orders.MarkOrderFailed.MarkOrderFailedCommand;
using AvroMarkOrderFailedCommand = Ordering.Orders.MarkOrderFailedCommand;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>MarkOrderFailedCommand</c> on
/// <c>ordering.order-commands</c> and dispatches it to the application
/// handler. Terminal failure — downstream Checkout saga compensation
/// relies on the emitted <c>OrderFailedEvent</c>.
/// </summary>
internal sealed class MarkOrderFailedCommandKafkaHandler
    : SagaCommandHandlerBase<AvroMarkOrderFailedCommand>, IMessageHandler<AvroMarkOrderFailedCommand>
{
    private readonly ICommandHandler<AppMarkOrderFailedCommand> _appHandler;

    public MarkOrderFailedCommandKafkaHandler(
        ICommandHandler<AppMarkOrderFailedCommand> appHandler,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        ILogger<MarkOrderFailedCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroMarkOrderFailedCommand message)
    {
        // ADR-0008 — Kafka header is the authoritative CorrelationId source.
        var correlationId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "CorrelationId header missing on Kafka message — ConsumerCorrelationIdMiddleware should have populated it.");

        return ExecuteAsync(context, correlationId, message.OrderId,
            ct => _appHandler.HandleAsync(message.ToAppCommand(), ct));
    }
}
