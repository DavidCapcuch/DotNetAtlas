using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Ordering's <c>OrderConfirmedEvent</c> on
/// <c>ordering.orders</c>. Upserts a <see cref="PendingInvoice"/> row keyed
/// on <c>CorrelationId</c>, populating the order half. When the payment
/// half is already present, marks the row converged via
/// <see cref="PendingInvoice.CompletedAtUtc"/> — M7's
/// <c>IssueInvoiceCommandHandler</c> picks up converged rows from there.
/// </summary>
/// <remarks>
/// <para>
/// Inbox-dedup middleware (<c>Platform.KafkaFlow.Inbox.EFCore</c>) runs in
/// front of this handler — duplicate <c>MessageId</c> redeliveries skip the
/// handler entirely. The handler additionally tolerates same-payload
/// resubmissions (different <c>MessageId</c>) by detecting a row whose
/// order half is already populated and treating it as a no-op.
/// </para>
/// <para>
/// The inbox middleware owns the surrounding transaction; the handler just
/// calls <see cref="IInvoicingDbContext.SaveChangesAsync"/> and the
/// middleware commits both the inbox row and the projection mutation
/// atomically.
/// </para>
/// </remarks>
internal sealed class OrderConfirmedInvoiceProjectionKafkaHandler
    : IMessageHandler<AvroOrderConfirmedEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderConfirmedInvoiceProjectionKafkaHandler> _logger;

    public OrderConfirmedInvoiceProjectionKafkaHandler(
        IInvoicingDbContext db,
        TimeProvider timeProvider,
        ILogger<OrderConfirmedInvoiceProjectionKafkaHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroOrderConfirmedEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        var ct = context.ConsumerContext.WorkerStopped;
        var now = _timeProvider.GetUtcNow();
        var orderJson = SerializePayload(message);

        using var correlationScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["OrderId"] = message.OrderId,
            ["BuyerId"] = message.BuyerId,
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingInvoices,
            message.CorrelationId,
            () => new PendingInvoice
            {
                CorrelationId = message.CorrelationId,
                OrderId = message.OrderId,
                BuyerId = message.BuyerId,
                OrderPayload = orderJson,
                FirstSeenAtUtc = now,
            },
            ct);

        if (!isNew)
        {
            if (row.OrderId is not null)
            {
                // Order half already captured under this CorrelationId — same-payload duplicate.
                _logger.LogInformation(
                    "OrderConfirmedEvent already projected for CorrelationId {CorrelationId}; no-op.",
                    message.CorrelationId);
                return;
            }

            row.OrderId = message.OrderId;
            row.BuyerId = message.BuyerId;
            row.OrderPayload = orderJson;

            if (row.PaymentId is not null && row.CompletedAtUtc is null)
            {
                row.CompletedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "OrderConfirmedEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);
    }

    private static string SerializePayload(AvroOrderConfirmedEvent message)
    {
        // Hand-rolled DTO instead of JsonSerializer.Serialize(message) — the auto-generated
        // Avro class exposes a Schema property of type Avro.Schema that breaks
        // System.Text.Json reflection serialisation. Listing the data fields explicitly
        // also future-proofs against avrogen reshaping the record (a regenerated class
        // with new internals would still produce stable JSON for M7 hydration).
        return JsonSerializer.Serialize(new
        {
            message.OrderId,
            message.CorrelationId,
            message.BuyerId,
            message.ConfirmedAtUtc,
        });
    }
}
