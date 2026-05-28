using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Errors;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;

namespace Inventory.Infrastructure.Messaging.Kafka.StockInit;

/// <summary>
/// Consumes Ordering's <c>OrderCancelledEvent</c> on
/// <c>ordering.orders</c> and releases every still-Active reservation tied
/// to the cancelled order
/// (<c>inventory.md</c> &lt;contract&gt; line 314 — "release-if-still-reserved").
/// </summary>
/// <remarks>
/// <para>
/// Avro <see cref="AvroOrderCancelledEvent"/> carries only
/// <c>OrderId</c>+<c>CorrelationId</c>+<c>Reason</c>+<c>AtStatus</c>+<c>CancelledAtUtc</c>
/// — there is no per-reservation list. The handler queries
/// <c>reservation_audit WHERE OrderId = msg.OrderId AND Status = Active</c>
/// and dispatches one <see cref="ReleaseReservationCommand"/> per row with
/// <see cref="ReleaseReason.Cancellation"/>.
/// </para>
/// <para>
/// Diverges from <see cref="SagaCommandHandlerBase{T}"/>: that wrapper
/// throws <c>SagaCommandDispatchException</c> on the first
/// <c>Result.Fail</c> and routes the message to DLT. For the cancellation
/// fan-out we want partial success to be retried by KafkaFlow's
/// <c>RetryForever</c> (the inbox tx rolls back, the next attempt re-queries
/// active reservations and only retries the still-pending ones — naturally
/// idempotent). So we throw a <see cref="DbUpdateException"/> instead of
/// <c>SagaCommandDispatchException</c> when a release fails: the retry
/// middleware (which lists <c>DbUpdateException</c> in its retry list)
/// re-runs the whole pipeline.
/// </para>
/// <para>
/// Same-message redelivery is naturally idempotent: the audit query filters
/// <c>Status = Active</c>; on retry the released ones drop out automatically.
/// </para>
/// </remarks>
internal sealed class OrderCancelledEventKafkaHandler : IMessageHandler<AvroOrderCancelledEvent>
{
    private readonly ICommandHandler<ReleaseReservationCommand> _appHandler;
    private readonly IInventoryDbContext _dbContext;
    private readonly ITransactionalOutbox<IInventoryDbContext> _transactionalOutbox;
    private readonly ILogger<OrderCancelledEventKafkaHandler> _logger;

    public OrderCancelledEventKafkaHandler(
        ICommandHandler<ReleaseReservationCommand> appHandler,
        IInventoryDbContext dbContext,
        ITransactionalOutbox<IInventoryDbContext> transactionalOutbox,
        ILogger<OrderCancelledEventKafkaHandler> logger)
    {
        _appHandler = appHandler;
        _dbContext = dbContext;
        _transactionalOutbox = transactionalOutbox;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroOrderCancelledEvent message)
    {
        // ADR-0008 — Kafka header is the authoritative CorrelationId source; Avro payload field
        // is convenience metadata only.
        var correlationId = context.ExtractCorrelationId()
            ?? throw new InvalidOperationException(
                "CorrelationId header missing on Kafka message — ConsumerCorrelationIdMiddleware should have populated it.");

        var origin = context.ExtractOrigin();
        var cancellationToken = context.ConsumerContext.WorkerStopped;

        using var correlationScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["OrderId"] = message.OrderId,
            ["AtStatus"] = message.AtStatus,
        });

        var occurredOnUtc = new DateTimeOffset(
            DateTime.SpecifyKind(message.CancelledAtUtc, DateTimeKind.Utc),
            TimeSpan.Zero);

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            // Snapshot the active reservations BEFORE dispatching releases.
            // AsNoTracking because the projection rows are mutated by the
            // Application layer's projection handler during dispatch — we
            // only need a snapshot of (ReservationId, ProductId) pairs.
            var activeReservations = await _dbContext.ReservationAudit
                .AsNoTracking()
                .Where(r => r.OrderId == message.OrderId && r.Status == ReservationStatus.Active)
                .Select(r => new { r.ReservationId, r.ProductId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Received OrderCancelledEvent from origin {Origin}: {ActiveCount} active reservations to release",
                origin ?? "unknown", activeReservations.Count);

            if (activeReservations.Count == 0)
            {
                // Nothing to do — order had no Active reservations. This is
                // the steady-state for orders cancelled before stock was
                // reserved (AtStatus=Created) or after compensation already
                // ran. Safe no-op.
                return;
            }

            foreach (var reservation in activeReservations)
            {
                var releaseCommand = new ReleaseReservationCommand
                {
                    ReservationId = reservation.ReservationId,
                    ProductId = reservation.ProductId,
                    Reason = ReleaseReason.Cancellation,
                    OccurredOnUtc = occurredOnUtc,
                    CorrelationId = correlationId,
                };

                var result = await _appHandler.HandleAsync(releaseCommand, cancellationToken).ConfigureAwait(false);

                if (result.IsFailed)
                {
                    var errorSummary = string.Join("; ", result.Errors.Select(e => e.Message));
                    _logger.LogWarning(
                        "Release failed for reservation {ReservationId} (Product {ProductId}); rethrowing as DbUpdateException so KafkaFlow RetryForever re-runs the message: {Errors}",
                        reservation.ReservationId, reservation.ProductId, errorSummary);

                    // Throw a retry-eligible exception so KafkaFlow's
                    // RetryForever middleware classifies this as transient
                    // and re-runs the message. The inbox tx rolls back; the
                    // next attempt re-queries Active reservations
                    // (already-released ones drop out) and retries the still-
                    // pending ones. Idempotent by construction.
                    //
                    // Use a dedicated DbUpdateException subclass so a DLT
                    // post-mortem can identify the failure point by type and
                    // read structured ReservationId/OrderId/ErrorCodes off
                    // the .Data dictionary — mirrors SagaCommandDispatchException's
                    // role for the saga-command consumers.
                    throw new ReservationReleaseFailedException(
                        reservationId: reservation.ReservationId,
                        orderId: message.OrderId,
                        errorSummary: errorSummary,
                        errorCodes: result.Errors
                            .OfType<DomainError>()
                            .Select(e => e.ErrorCode)
                            .ToArray());
                }
            }

            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "OrderCancelledEvent handled successfully: released {Count} reservations for order {OrderId}",
                activeReservations.Count, message.OrderId);
        }, cancellationToken);
    }
}

/// <summary>
/// Thrown by <see cref="OrderCancelledEventKafkaHandler"/> when a
/// per-reservation <c>ReleaseReservationCommand</c> dispatch returns
/// <c>Result.Fail</c>. Subclasses <see cref="DbUpdateException"/> so KafkaFlow's
/// <c>RetryForever</c> middleware (which retries on <c>DbUpdateException</c>)
/// re-runs the message; the dedicated type plus the structured
/// <see cref="Exception.Data"/> entries (<c>ReservationId</c>, <c>OrderId</c>,
/// <c>ErrorCodes</c>) give operators a faster path through a DLT post-mortem
/// than a bare <c>DbUpdateException</c> would.
/// </summary>
public sealed class ReservationReleaseFailedException : DbUpdateException
{
    public ReservationReleaseFailedException(
        Guid reservationId,
        Guid orderId,
        string errorSummary,
        string[] errorCodes)
        : base($"Release of reservation {reservationId} for order {orderId} failed: {errorSummary}")
    {
        Data["ReservationId"] = reservationId;
        Data["OrderId"] = orderId;
        Data["ErrorCodes"] = errorCodes;
    }

    public ReservationReleaseFailedException()
    {
    }

    public ReservationReleaseFailedException(string message)
        : base(message)
    {
    }

    public ReservationReleaseFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
