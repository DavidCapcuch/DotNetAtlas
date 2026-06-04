using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Application.Common.Messaging;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Reservations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Inventory.Application.StockItems.ReserveStock;

/// <summary>
/// Handles the saga's reservation intent. The success path is standard:
/// delegate to the event store, let the <c>StockReservedDomainEvent</c> dispatch
/// trigger the projection + outbox publisher handlers.
/// </summary>
/// <remarks>
/// <para>
/// The failure path is special. <c>InsufficientStockError</c> is
/// business-expected and MUST surface to the saga as an outbox-backed
/// <c>StockReservationFailedEvent</c>, never a throw — the aggregate emits
/// no ES event on failure (nothing to project), so the external Avro event
/// is assembled here and added to the outbox directly. The handler then
/// calls <see cref="ITransactionalOutbox{TContext}.SaveChangesAsync"/>
/// to commit the outbox row.
/// </para>
/// </remarks>
internal sealed class ReserveStockCommandHandler : ICommandHandler<ReserveStockCommand>
{
    /// <summary>Service-default TTL matching <c>inventory.md</c> § 11 (15 minutes).</summary>
    internal static readonly TimeSpan DefaultReservationTtl = TimeSpan.FromMinutes(15);

    private readonly IEventStore _eventStore;
    private readonly ITransactionalOutbox<IInventoryDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<ReserveStockCommandHandler> _logger;

    public ReserveStockCommandHandler(
        IEventStore eventStore,
        ITransactionalOutbox<IInventoryDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<ReserveStockCommandHandler> logger)
    {
        _eventStore = eventStore;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReserveStockCommand command, CancellationToken ct)
    {
        var reservationIdResult = ReservationId.Create(command.ReservationId);
        if (reservationIdResult.IsFailed)
        {
            return reservationIdResult.ToResult();
        }

        var ttl = command.TimeToLive ?? DefaultReservationTtl;

        var result = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.Reserve(
                reservationIdResult.Value,
                command.Quantity,
                command.OrderId,
                ttl,
                command.OccurredOnUtc).ToResult(),
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Reserved {Quantity} units of Product {ProductId} for Order {OrderId} (reservation {ReservationId}, version after append: {Version})",
                command.Quantity, command.ProductId, command.OrderId, command.ReservationId, result.Value.Version);
            return Result.Ok();
        }

        // Business-expected failure: InsufficientStock. Publish the external
        // failure event via the outbox — no ES event was appended, so nothing
        // triggers a normal outbox publisher; assemble here and commit.
        //
        // Other failure error types (e.g. ConcurrencyError after retry, or
        // future business errors raised by the aggregate) are intentionally
        // NOT mapped to a saga-visible event here — they indicate transient
        // infrastructure conditions or caller bugs that the saga should treat
        // as retryable (ConcurrencyError) or as poison (DataIntegrityException
        // → DLT). If a new business error needs a dedicated saga response,
        // add an explicit branch above this comment rather than threading it
        // through the catch-all return at the bottom.
        var insufficient = result.Errors.OfType<InsufficientStockError>().FirstOrDefault();
        if (insufficient is not null)
        {
            var avro = new StockReservationFailedEvent
            {
                ProductId = command.ProductId,
                OrderId = command.OrderId,
                RequestedQuantity = insufficient.Requested,
                AvailableQuantity = insufficient.Available,
                FailedAtUtc = command.OccurredOnUtc.UtcDateTime,
            };

            _outbox.AddOutboxMessage(
                _topics.InventoryReservations,
                command.OrderId.ToString(),
                avro);

            await _outbox.SaveChangesAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "InsufficientStock on reserve request for Product {ProductId} (requested {Requested}, available {Available}) — Order {OrderId}; StockReservationFailedEvent queued",
                command.ProductId, insufficient.Requested, insufficient.Available, command.OrderId);
        }

        return result.ToResult();
    }
}
