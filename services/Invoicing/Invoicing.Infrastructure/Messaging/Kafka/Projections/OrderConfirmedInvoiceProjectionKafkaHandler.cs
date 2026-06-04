using System.Text.Json;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Invoices.Projections;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.KafkaFlow.Inbox.EFCore;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;

namespace Invoicing.Infrastructure.Messaging.Kafka.Projections;

/// <summary>
/// Inbound consumer for Ordering's <c>OrderConfirmedEvent</c> on
/// <c>ordering.orders</c>. Upserts a <see cref="PendingInvoice"/> row keyed
/// on <c>OrderId</c>, populating the order half. When the payment
/// half is already present, marks the row converged via
/// <see cref="PendingInvoice.CompletedAtUtc"/> AND dispatches
/// <see cref="IssueInvoiceCommand"/> in the same inbox transaction so the
/// projection update + invoice insert + outbox row commit atomically.
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
/// The inbox middleware owns the surrounding transaction; the handler calls
/// <see cref="IInvoicingDbContext.SaveChangesAsync"/> + (on convergence)
/// dispatches the command, and the middleware commits everything together.
/// The command handler detects the open transaction and joins it rather than
/// nesting (see <see cref="IssueInvoiceCommandHandler"/>'s <c>ownsTransaction</c>
/// branch).
/// </para>
/// </remarks>
internal sealed class OrderConfirmedInvoiceProjectionKafkaHandler
    : IMessageHandler<AvroOrderConfirmedEvent>
{
    private readonly IInvoicingDbContext _db;
    private readonly ICommandHandler<IssueInvoiceCommand, Guid> _issueInvoiceHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderConfirmedInvoiceProjectionKafkaHandler> _logger;

    public OrderConfirmedInvoiceProjectionKafkaHandler(
        IInvoicingDbContext db,
        ICommandHandler<IssueInvoiceCommand, Guid> issueInvoiceHandler,
        TimeProvider timeProvider,
        ILogger<OrderConfirmedInvoiceProjectionKafkaHandler> logger)
    {
        _db = db;
        _issueInvoiceHandler = issueInvoiceHandler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AvroOrderConfirmedEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        // ADR-0029/0030 — OrderId is the cross-BC convergence key; both halves carry it.
        var orderId = message.OrderId;
        var ct = context.ConsumerContext.WorkerStopped;
        var now = _timeProvider.GetUtcNow();
        var orderJson = SerializePayload(message);

        // ADR-0008 — log/trace correlation flows from the Kafka header via
        // Serilog LogContext. Do not push "CorrelationId" into this BeginScope;
        // the inner-most scope would shadow the header-authoritative value.
        using var localScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OrderId"] = orderId,
            ["BuyerId"] = message.BuyerId,
        });

        var (row, isNew) = await PendingProjectionUpsertHelper.GetOrAddAsync(
            _db.PendingInvoices,
            orderId,
            () => new PendingInvoice
            {
                OrderId = orderId,
                BuyerId = message.BuyerId,
                OrderPayload = orderJson,
                FirstSeenAtUtc = now,
            },
            ct);

        var convergedNow = false;
        if (!isNew)
        {
            if (row.OrderPayload is not null)
            {
                // Order half already captured for this OrderId — same-payload duplicate.
                _logger.LogInformation(
                    "OrderConfirmedEvent already projected for OrderId {OrderId}; no-op.",
                    orderId);
                return;
            }

            row.BuyerId = message.BuyerId;
            row.OrderPayload = orderJson;

            if (row.PaymentId is not null && row.CompletedAtUtc is null)
            {
                row.CompletedAtUtc = now;
                convergedNow = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "OrderConfirmedEvent projected (IsNew={IsNew}, Converged={Converged})",
            isNew,
            row.CompletedAtUtc is not null);

        if (convergedNow)
        {
            // M7 — dispatch the issuance command inside the inbox transaction so the
            // pending_invoices update, the new Invoice aggregate, and the outbox row
            // commit atomically. Failures bubble up; the inbox middleware rolls back.
            // The command handler is idempotent on IssuedInvoiceId so a retry that
            // re-runs convergence from the OTHER half (PaymentCaptured) is safe.
            var result = await _issueInvoiceHandler.HandleAsync(
                new IssueInvoiceCommand { OrderId = orderId },
                ct);
            if (result.IsFailed)
            {
                throw new InvalidOperationException(
                    $"IssueInvoiceCommand failed after convergence on OrderId {orderId}: "
                        + string.Join("; ", result.Errors.Select(e => e.Message)));
            }
        }
    }

    private static string SerializePayload(AvroOrderConfirmedEvent message)
    {
        // Hand-rolled DTO instead of JsonSerializer.Serialize(message) — the auto-generated
        // Avro class exposes a Schema property of type Avro.Schema that breaks
        // System.Text.Json reflection serialisation. Listing the data fields explicitly
        // also future-proofs against avrogen reshaping the record (a regenerated class
        // with new internals would still produce stable JSON for M7 hydration).
        //
        // Per ADR-0020 (Wave 1.5) the Avro event is a Summary Event — Items, TotalAmount,
        // Currency and BillingAddress travel with it. Persisting them into OrderPayload
        // jsonb means M7's IssueInvoiceCommandHandler can construct Invoice.Create(...)
        // from the converged pending_invoices row without an HTTP round-trip.
        //
        // Wave-1 deferral (closeout1 M10, issue #133): BillingAddress lands here as
        // plaintext while the Invoice aggregate's _enc columns reserve the contract
        // for v2 DEK encryption. Dropping the address would break M7's hydration; the
        // proper fix is v2 parity (encrypted jsonb or a separate _enc projection table)
        // which requires a migration the user generates.
        return JsonSerializer.Serialize(new
        {
            message.OrderId,
            message.BuyerId,
            message.ConfirmedAtUtc,
            Items = message.Items?.Select(i => new
            {
                i.ProductId,
                i.Sku,
                i.Name,
                i.Quantity,
                UnitPriceAmount = (decimal)i.UnitPriceAmount,
                LineTotalAmount = (decimal)i.LineTotalAmount,
            }).ToList(),
            TotalAmount = message.TotalAmount.HasValue ? (decimal?)message.TotalAmount.Value : null,
            message.Currency,
            BillingAddress = message.BillingAddress is null ? null : new
            {
                message.BillingAddress.Street1,
                message.BillingAddress.Street2,
                message.BillingAddress.City,
                message.BillingAddress.State,
                message.BillingAddress.PostalCode,
                message.BillingAddress.CountryCode,
            },
        });
    }
}
