using Inventory.Application.Common.Data;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppReleaseReservationCommand = Inventory.Application.StockItems.ReleaseReservation.ReleaseReservationCommand;
using AvroReleaseReservationCommand = Inventory.Reservations.ReleaseReservationCommand;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>ReleaseReservationCommand</c> on
/// <c>inventory.reservation-commands</c> and dispatches it to the
/// application handler. Three sources upstream: saga compensation, the
/// M6 TTL-expiry worker, and admin / customer cancel — distinguished by
/// <see cref="Inventory.Domain.StockItems.ValueObjects.ReleaseReason"/>
/// which propagates unchanged into the external
/// <c>ReservationReleasedEvent</c>.
/// </summary>
internal sealed class ReleaseReservationCommandKafkaHandler
    : SagaCommandHandlerBase<AvroReleaseReservationCommand>, IMessageHandler<AvroReleaseReservationCommand>
{
    private readonly ICommandHandler<AppReleaseReservationCommand> _appHandler;

    public ReleaseReservationCommandKafkaHandler(
        ICommandHandler<AppReleaseReservationCommand> appHandler,
        ITransactionalOutbox<IInventoryDbContext> transactionalOutbox,
        ILogger<ReleaseReservationCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroReleaseReservationCommand message)
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
                ["ReleaseReason"] = message.ReleaseReason,
            },
            ct => _appHandler.HandleAsync(message.ToAppCommand(correlationId), ct));
    }
}
