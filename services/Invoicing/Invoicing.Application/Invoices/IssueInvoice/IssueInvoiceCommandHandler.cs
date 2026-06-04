using System.Globalization;
using System.Text.Json;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Exceptions;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.Pdf;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Application.Invoices.IssueInvoice;

/// <summary>
/// Handles <see cref="IssueInvoiceCommand"/> — the happy path that turns a converged
/// <c>pending_invoices</c> row into an <c>Invoice</c> aggregate, allocates a gap-free
/// invoice number, renders + uploads the PDF, and atomically writes the aggregate +
/// outbox row + projection-row update inside a single EF transaction.
/// </summary>
/// <remarks>
/// <para>
/// Transaction shape is dictated by the gap-free allocator (ADR-0018) — the allocator
/// asserts <c>Database.CurrentTransaction is not null</c> and the surrounding
/// <c>PostgresInvoiceNumberAllocator</c>'s <c>SELECT ... FOR UPDATE</c> requires
/// the same scope as the aggregate insert. <c>EnableRetryOnFailure</c> is intentionally
/// off (see <c>PersistenceDependencyInjection.AddPersistence</c>); transient-failure
/// recovery is delegated to the enrichment-projection consumers re-firing this command
/// on the next observation of convergence.
/// </para>
/// <para>
/// Idempotency: short-circuits when <c>PendingInvoice.IssuedInvoiceId</c> is already set
/// for this <see cref="IssueInvoiceCommand.OrderId"/>. The unique index on
/// <c>invoices.order_id</c> is defence-in-depth — a successful insert past the
/// short-circuit would still trip the DB constraint if two consumers raced past
/// the projection-row check.
/// </para>
/// <para>
/// Failure semantics:
/// <list type="bullet">
///   <item><description><b>Total mismatch</b> (<c>OrderConfirmedEvent.TotalAmount</c> ≠
///     <c>PaymentCapturedEvent.Amount</c>): bug-class — throws
///     <see cref="InvoiceTotalMismatchException"/> (a <see cref="DataIntegrityException"/>
///     subclass carrying the source values as typed properties); consumer DLT'd.</description></item>
///   <item><description><b>Missing summary fields</b> on <c>OrderConfirmedEvent</c>
///     (Items / TotalAmount / Currency / BillingAddress null in the persisted JSON,
///     even though they are required in production): bug-class — throws
///     <see cref="DataIntegrityException"/>.</description></item>
///   <item><description><b>VO validation failures</b> on <c>InvoiceLine.Create</c> /
///     <c>Money.Create</c> / <c>Address.Create</c>: bug-class — the upstream BCs
///     guarantee shape; failures surface as <see cref="DataIntegrityException"/>.</description></item>
///   <item><description><b>Blob-upload exceptions</b>: <see cref="CriticalException"/>
///     bubbles to the consumer middleware and is DLT'd; the SDK has already exhausted
///     its retry budget by then.</description></item>
/// </list>
/// </para>
/// <para>
/// V1 simplification — VAT: the producer-side <c>OrderConfirmedEvent</c> Avro schema does
/// not carry per-line VAT rates, so every line is recorded at 0% (<see cref="VatRate"/>
/// 0.00) with an empty <see cref="VatLine"/> breakdown. This satisfies invariant I-1
/// trivially (Total == Subtotal); v2 will add rate plumbing through Ordering.
/// </para>
/// </remarks>
internal sealed class IssueInvoiceCommandHandler : ICommandHandler<IssueInvoiceCommand, Guid>
{
    private const string PdfContentType = "application/pdf";

    private readonly IInvoicingDbContext _db;
    private readonly IInvoiceNumberAllocator _numberAllocator;
    private readonly IPdfGenerator _pdfGenerator;
    private readonly IBlobStore _blobStore;
    private readonly BlobStorageOptions _blobOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IssueInvoiceCommandHandler> _logger;

    public IssueInvoiceCommandHandler(
        IInvoicingDbContext db,
        IInvoiceNumberAllocator numberAllocator,
        IPdfGenerator pdfGenerator,
        IBlobStore blobStore,
        IOptions<BlobStorageOptions> blobOptions,
        TimeProvider timeProvider,
        ILogger<IssueInvoiceCommandHandler> logger)
    {
        _db = db;
        _numberAllocator = numberAllocator;
        _pdfGenerator = pdfGenerator;
        _blobStore = blobStore;
        _blobOptions = blobOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(IssueInvoiceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Bug-class — the consumer raised this command, so the row must exist.
        var pending = await _db.PendingInvoices
            .FirstOrDefaultAsync(r => r.OrderId == command.OrderId, ct)
            ?? throw new DataIntegrityException(
                "Invoicing.PendingInvoiceMissing",
                $"No pending_invoices row for OrderId '{command.OrderId}'.");

        if (pending.IssuedInvoiceId is { } already)
        {
            _logger.LogInformation(
                "IssueInvoiceCommand replayed for OrderId {OrderId}; invoice {InvoiceId} already issued.",
                command.OrderId,
                already);
            return Result.Ok(already);
        }

        if (pending.OrderPayload is null || pending.PaymentPayload is null
            || pending.PaymentId is null
            || pending.CompletedAtUtc is null)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingInvoiceNotConverged",
                $"pending_invoices row {command.OrderId} is not converged (Order/Payment payload missing).");
        }

        var orderPayload = DeserializeOrderPayload(pending.OrderPayload, command.OrderId);
        var paymentPayload = DeserializePaymentPayload(pending.PaymentPayload, command.OrderId);

        // Cross-aggregate consistency (example-mapping 1.4): Order.Total == Payment.Amount.
        // TotalAmount is decimal? in the JSON shape because the underlying Avro field is a
        // FORWARD_TRANSITIVE-nullable union; null was already rejected by the deserializer's
        // required-field check above.
        var orderTotal = orderPayload.TotalAmount!.Value;
        if (orderTotal != paymentPayload.Amount
            || !string.Equals(orderPayload.Currency, paymentPayload.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvoiceTotalMismatchException(orderTotal, paymentPayload.Amount, command.OrderId);
        }

        if (pending.BuyerId is null)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingInvoiceMissingBuyer",
                $"pending_invoices row {command.OrderId} has no BuyerId despite both halves present.");
        }

        var billingAddress = ToAddress(orderPayload.BillingAddress!);
        var currency = ResolveCurrency(orderPayload.Currency!);
        var lines = BuildInvoiceLines(orderPayload.Items!, currency);
        var deliveryChannel = DeliveryChannel.Email; // M8 ships SendEmailNotificationCommand fan-out via InvoiceDeliveryRequestedOutboxPublisher.

        // ADR-0018 — the allocator demands an enclosing transaction. When dispatched from
        // the M6 consumer, the inbox middleware already owns a transaction (it wraps the
        // consumer's Handle() body so dedup + projection write commit atomically); in that
        // case we must NOT begin a nested one. When called standalone (e.g. integration
        // tests, future scheduled worker) we own the transaction here.
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        var invoiceNumber = await _numberAllocator.AllocateAsync(ct);

        var utcNow = _timeProvider.GetUtcNow();
        var createResult = Invoice.Create(
            buyerId: pending.BuyerId.Value,
            orderId: pending.OrderId,
            paymentId: pending.PaymentId.Value,
            billingAddress: billingAddress,
            lines: lines,
            vatLines: [],
            deliveryChannel: deliveryChannel,
            utcNow: utcNow);
        if (createResult.IsFailed)
        {
            // Factory-side failures are bug-class — every Result.Fail in Invoice.Create reflects
            // a contract that the upstream caller (us, here) was expected to honour.
            throw new DataIntegrityException(
                "Invoicing.InvoiceFactoryFailed",
                string.Join("; ", createResult.Errors.Select(e => e.Message)));
        }

        var invoice = createResult.Value;

        // Lock the produced Invoice.Total against the source-of-truth Order total. v1's empty-
        // VatLines simplification means Invoice.Total == Subtotal == sum(line totals); if the
        // upstream OrderConfirmedEvent ever ships totals that include VAT/discounts/shipping,
        // this assertion DLT's the message immediately rather than emitting a wrong
        // InvoiceIssuedEvent.Total downstream. Locked-in by the schema-level invariant
        // ("Total == OrderConfirmedEvent.TotalAmount") on InvoiceIssuedEvent.avsc.
        if (invoice.Total.Amount != orderTotal)
        {
            throw new DataIntegrityException(
                "Invoicing.InvoiceTotalDriftFromOrder",
                $"Invoice.Total ({invoice.Total.Amount}) does not equal OrderConfirmedEvent.TotalAmount ({orderTotal}); "
                    + "v1 invariant violated — Ordering must emit pre-VAT totals or v2 must add per-line VAT plumbing.");
        }

        // Stamp the number BEFORE PDF rendering so the renderer can include it in the
        // document body (per InvoiceDocument template line 79). The split-issue flow
        // resolves the chicken-and-egg: PDF needs InvoiceNumber, Issue needs PdfBlobRef.
        invoice.AssignInvoiceNumber(invoiceNumber);

        var pdfResult = await _pdfGenerator.GenerateInvoiceAsync(invoice, ct);

        var blobName = InvoicePdfBlobName.For(invoiceNumber);
        var pdfBlobRef = await _blobStore.UploadAsync(
            _blobOptions.InvoicesContainerName,
            blobName,
            pdfResult.Content,
            PdfContentType,
            metadata: null,
            ct);

        var issueResult = invoice.Issue(pdfBlobRef, utcNow);
        if (issueResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Invoicing.InvoiceIssueFailed",
                string.Join("; ", issueResult.Errors.Select(e => e.Message)));
        }

        _db.Invoices.Add(invoice);

        // Mark the projection row issued in the same transaction so a redelivery of either
        // half is observably a no-op.
        pending.IssuedInvoiceId = invoice.Id;

        await _db.SaveChangesAsync(ct);

        if (ownsTransaction)
        {
            await transaction!.CommitAsync(ct);
        }

        _logger.LogInformation(
            "Issued invoice {InvoiceNumber} ({InvoiceId}) for OrderId {OrderId}; total={Total} {Currency}.",
            invoiceNumber.Value,
            invoice.Id,
            command.OrderId,
            invoice.Total.Amount.ToString(CultureInfo.InvariantCulture),
            invoice.Total.Currency.Name);

        return Result.Ok(invoice.Id);
    }

    private static List<InvoiceLine> BuildInvoiceLines(
        IReadOnlyList<OrderItemPayload> items,
        CurrencyCode currency)
    {
        var zeroVat = VatRate.Create(0m).Value;
        var lines = new List<InvoiceLine>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var skuResult = Sku.Create(item.Sku);
            if (skuResult.IsFailed)
            {
                throw new DataIntegrityException(
                    "Invoicing.InvalidSkuOnOrderPayload",
                    $"OrderConfirmedEvent line {i + 1} carries invalid Sku '{item.Sku}'.");
            }

            var unitPriceResult = Money.Create(item.UnitPriceAmount, currency);
            if (unitPriceResult.IsFailed)
            {
                throw new DataIntegrityException(
                    "Invoicing.InvalidUnitPriceOnOrderPayload",
                    $"OrderConfirmedEvent line {i + 1} carries invalid unit price {item.UnitPriceAmount}.");
            }

            var lineResult = InvoiceLine.Create(
                lineNumber: i + 1,
                sku: skuResult.Value,
                description: item.Name,
                quantity: item.Quantity,
                unitPrice: unitPriceResult.Value,
                vatRate: zeroVat);
            if (lineResult.IsFailed)
            {
                throw new DataIntegrityException(
                    "Invoicing.InvalidInvoiceLine",
                    $"InvoiceLine.Create failed for OrderConfirmedEvent line {i + 1}: "
                        + string.Join("; ", lineResult.Errors.Select(e => e.Message)));
            }

            lines.Add(lineResult.Value);
        }

        return lines;
    }

    private static Address ToAddress(OrderBillingAddressPayload addr)
    {
        var result = Address.Create(addr.Street1, addr.Street2, addr.City, addr.State, addr.PostalCode, addr.CountryCode);
        if (result.IsFailed)
        {
            throw new DataIntegrityException(
                "Invoicing.InvalidBillingAddressOnOrderPayload",
                "OrderConfirmedEvent.BillingAddress failed Address.Create: "
                    + string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return result.Value;
    }

    private static CurrencyCode ResolveCurrency(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
        {
            throw new DataIntegrityException(
                "Invoicing.InvalidCurrencyCodeOnOrderPayload",
                $"OrderConfirmedEvent.Currency '{code}' is not a valid ISO 4217 code.");
        }

        if (!CurrencyCode.TryFromName(code.ToUpperInvariant(), out var currency))
        {
            throw new DataIntegrityException(
                "Invoicing.UnknownCurrencyCodeOnOrderPayload",
                $"Unknown ISO 4217 currency code '{code}' on OrderConfirmedEvent.");
        }

        return currency;
    }

    private static OrderPayload DeserializeOrderPayload(string json, Guid orderId)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<OrderPayload>(json, JsonOptions.Default);
            if (payload is null
                || payload.Items is null
                || payload.TotalAmount is null
                || payload.Currency is null
                || payload.BillingAddress is null)
            {
                throw new DataIntegrityException(
                    "Invoicing.PendingInvoiceMissingSummary",
                    $"OrderConfirmedEvent payload for {orderId} is missing required summary fields (Items / TotalAmount / Currency / BillingAddress).");
            }

            return payload;
        }
        catch (JsonException ex)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingInvoiceCorruptOrderPayload",
                $"OrderConfirmedEvent payload for {orderId} is not valid JSON: {ex.Message}");
        }
    }

    private static PaymentPayload DeserializePaymentPayload(string json, Guid orderId)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PaymentPayload>(json, JsonOptions.Default);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Currency))
            {
                throw new DataIntegrityException(
                    "Invoicing.PendingInvoicePaymentPayloadIncomplete",
                    $"PaymentCapturedEvent payload for {orderId} is incomplete.");
            }

            return payload;
        }
        catch (JsonException ex)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingInvoiceCorruptPaymentPayload",
                $"PaymentCapturedEvent payload for {orderId} is not valid JSON: {ex.Message}");
        }
    }

    private static class JsonOptions
    {
        // Match the M6 producer-side handler: System.Text.Json default casing (PascalCase
        // properties), which is what OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload
        // emits. Centralised so M7 deserialisation tracks any future M6 producer changes.
        internal static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    /// <summary>Mirrors the JSON shape emitted by <c>OrderConfirmedInvoiceProjectionKafkaHandler.SerializePayload</c>.</summary>
    private sealed record OrderPayload
    {
        public required Guid OrderId { get; init; }

        public required Guid BuyerId { get; init; }

        public required DateTime ConfirmedAtUtc { get; init; }

        public IReadOnlyList<OrderItemPayload>? Items { get; init; }

        public decimal? TotalAmount { get; init; }

        public string? Currency { get; init; }

        public OrderBillingAddressPayload? BillingAddress { get; init; }
    }

    private sealed record OrderItemPayload
    {
        public required Guid ProductId { get; init; }

        public required string Sku { get; init; }

        public required string Name { get; init; }

        public required int Quantity { get; init; }

        public required decimal UnitPriceAmount { get; init; }

        public required decimal LineTotalAmount { get; init; }
    }

    private sealed record OrderBillingAddressPayload
    {
        public required string Street1 { get; init; }

        public string? Street2 { get; init; }

        public required string City { get; init; }

        public string? State { get; init; }

        public required string PostalCode { get; init; }

        public required string CountryCode { get; init; }
    }

    /// <summary>Mirrors the JSON shape emitted by <c>PaymentCapturedInvoiceProjectionKafkaHandler.SerializePayload</c>.</summary>
    private sealed record PaymentPayload
    {
        public required Guid UserId { get; init; }

        public required Guid PaymentTransactionId { get; init; }

        public required string AuthorizationId { get; init; }

        public required decimal Amount { get; init; }

        public required string Currency { get; init; }

        public required DateTime CapturedAtUtc { get; init; }
    }
}
