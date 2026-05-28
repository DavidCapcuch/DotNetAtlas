using Inventory.Application.Common.Data;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppConfirmReservationCommand = Inventory.Application.StockItems.ConfirmReservation.ConfirmReservationCommand;
using AvroConfirmReservationCommand = Inventory.Reservations.ConfirmReservationCommand;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>ConfirmReservationCommand</c> on
/// <c>inventory.reservation-commands</c> and dispatches it to the
/// application handler. Idempotent on <c>ReservationId</c>: a second
/// confirm on an already-Confirmed reservation returns <c>Result.Ok</c>
/// with no event (per <c>inventory.md § 5.4</c>).
/// </summary>
internal sealed class ConfirmReservationCommandKafkaHandler
    : SagaCommandHandlerBase<AvroConfirmReservationCommand>, IMessageHandler<AvroConfirmReservationCommand>
{
    private readonly ICommandHandler<AppConfirmReservationCommand> _appHandler;

    public ConfirmReservationCommandKafkaHandler(
        ICommandHandler<AppConfirmReservationCommand> appHandler,
        ITransactionalOutbox<IInventoryDbContext> transactionalOutbox,
        ILogger<ConfirmReservationCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroConfirmReservationCommand message)
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
                ["ProductId"] = message.ProductId,
                ["ReservationId"] = message.ReservationId,
            },
            ct => _appHandler.HandleAsync(message.ToAppCommand(correlationId), ct));
    }
}
