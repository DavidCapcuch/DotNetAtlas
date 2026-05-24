using Inventory.Application.Common.Data;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppReserveStockCommand = Inventory.Application.StockItems.ReserveStock.ReserveStockCommand;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>ReserveStockCommand</c> on
/// <c>inventory.reservation-commands</c> and dispatches it to the
/// application handler. Idempotency is enforced by the KafkaFlow inbox
/// middleware (message-id dedup) plus the aggregate's own
/// <c>ReservationId</c>-keyed retention. <c>InsufficientStock</c> is a
/// business-expected outcome handled inside the application layer (which
/// emits <c>StockReservationFailedEvent</c> to the outbox) — this handler
/// only throws when the application returns a non-business
/// <see cref="FluentResults.Result.Fail(FluentResults.IError)"/>.
/// </summary>
internal sealed class ReserveStockCommandKafkaHandler
    : SagaCommandHandlerBase<AvroReserveStockCommand>, IMessageHandler<AvroReserveStockCommand>
{
    private readonly ICommandHandler<AppReserveStockCommand> _appHandler;

    public ReserveStockCommandKafkaHandler(
        ICommandHandler<AppReserveStockCommand> appHandler,
        ITransactionalOutbox<IInventoryDbContext> transactionalOutbox,
        ILogger<ReserveStockCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroReserveStockCommand message)
    {
        // ADR-0008 — Kafka header is the authoritative CorrelationId source; Avro payload field
        // is convenience metadata only.
        var correlationId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "CorrelationId header missing on Kafka message — ConsumerCorrelationIdMiddleware should have populated it.");

        return ExecuteAsync(
            context,
            correlationId,
            new Dictionary<string, object?>
            {
                ["OrderId"] = message.OrderId,
                ["ProductId"] = message.ProductId,
                ["ReservationId"] = message.ReservationId,
            },
            ct => _appHandler.HandleAsync(message.ToAppCommand(correlationId), ct));
    }
}
