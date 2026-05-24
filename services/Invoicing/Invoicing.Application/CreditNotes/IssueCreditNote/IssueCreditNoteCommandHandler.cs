using System.Globalization;
using System.Text.Json;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.Pdf;
using Invoicing.Domain.Common.Errors;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Application.CreditNotes.IssueCreditNote;

/// <summary>
/// Handles <see cref="IssueCreditNoteCommand"/> — issues a <see cref="CreditNote"/>
/// reversing a prior <see cref="Invoice"/> when both halves of the cancellation flow
/// (<c>OrderCancelledEvent</c> + <c>PaymentRefundedEvent</c>) have arrived. Atomically:
/// allocates a gap-free credit-note number, renders + uploads the PDF, persists the
/// credit note, transitions the original invoice to <c>Cancelled</c>, and writes both
/// outbox rows (<c>CreditNoteIssuedEvent</c> + <c>InvoiceCancelledEvent</c>) inside a
/// single EF transaction.
/// </summary>
/// <remarks>
/// <para>
/// V1 supports only full-amount reversals
/// (<see cref="CreditNoteReason.OrderCancelled"/>); partial refunds raise a
/// <c>PartialRefundNotSupportedV1</c> validation result so the upstream handler can
/// surface it without DLT'ing the message. The cross-aggregate guard (refund amount ==
/// invoice total) treats a mismatch as a bug-class condition (DataIntegrityException →
/// DLT) — a partial refund slipping past v1 means a contract violation upstream.
/// </para>
/// <para>
/// The handler hard-fails when the original invoice is missing because the example
/// mapping § 3 explicitly assumes the invoice was issued before cancellation. A refund
/// for an order with no prior invoice is an out-of-band scenario — DLT'd for ops
/// inspection. <c>InvoicingErrors.CreditNoteRefersToCancelledInvoice</c> covers the
/// already-cancelled invoice case via a recoverable <c>Result.Fail&lt;T&gt;</c>.
/// </para>
/// </remarks>
internal sealed class IssueCreditNoteCommandHandler : ICommandHandler<IssueCreditNoteCommand, Guid>
{
    private const string PdfContentType = "application/pdf";

    private readonly IInvoicingDbContext _db;
    private readonly ICreditNoteNumberAllocator _numberAllocator;
    private readonly IPdfGenerator _pdfGenerator;
    private readonly IBlobStore _blobStore;
    private readonly BlobStorageOptions _blobOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IssueCreditNoteCommandHandler> _logger;

    public IssueCreditNoteCommandHandler(
        IInvoicingDbContext db,
        ICreditNoteNumberAllocator numberAllocator,
        IPdfGenerator pdfGenerator,
        IBlobStore blobStore,
        IOptions<BlobStorageOptions> blobOptions,
        TimeProvider timeProvider,
        ILogger<IssueCreditNoteCommandHandler> logger)
    {
        _db = db;
        _numberAllocator = numberAllocator;
        _pdfGenerator = pdfGenerator;
        _blobStore = blobStore;
        _blobOptions = blobOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(IssueCreditNoteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var pending = await _db.PendingCreditNotes
            .FirstOrDefaultAsync(r => r.CorrelationId == command.CorrelationId, ct)
            ?? throw new DataIntegrityException(
                "Invoicing.PendingCreditNoteMissing",
                $"No pending_credit_notes row for CorrelationId '{command.CorrelationId}'.");

        if (pending.IssuedCreditNoteId is { } already)
        {
            _logger.LogInformation(
                "IssueCreditNoteCommand replayed for CorrelationId {CorrelationId}; credit note {CreditNoteId} already issued.",
                command.CorrelationId,
                already);
            return Result.Ok(already);
        }

        if (pending.OrderPayload is null || pending.PaymentPayload is null
            || pending.OrderId is null || pending.CompletedAtUtc is null)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingCreditNoteNotConverged",
                $"pending_credit_notes row {command.CorrelationId} is not converged.");
        }

        var orderPayload = DeserializeOrderPayload(pending.OrderPayload, command.CorrelationId);
        var paymentPayload = DeserializePaymentPayload(pending.PaymentPayload, command.CorrelationId);

        // Find the prior Invoice for this OrderId. Per design § 7.1 the credit-note path
        // requires an Issued/Delivered invoice — absence is bug-class (DLT'd).
        var originalInvoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == pending.OrderId.Value, ct)
            ?? throw new DataIntegrityException(
                "Invoicing.OriginalInvoiceMissing",
                $"No invoice found for OrderId '{pending.OrderId.Value}' (CorrelationId {command.CorrelationId}).");

        if (originalInvoice.Status == InvoiceStatus.Cancelled)
        {
            // Pre-factory short-circuit so we can return Result.Fail (recoverable) instead
            // of letting the factory throw — matches the C-N-1 user-error / bug-class split.
            return Result.Fail<Guid>(InvoicingErrors.CreditNoteRefersToCancelledInvoice(originalInvoice.Id));
        }

        // Currency mismatch is bug-class — currencies of an invoice and its refund must
        // ALWAYS match (Payments holds the captured currency on the original transaction;
        // refund is always in that currency). Throw → DLT.
        if (!string.Equals(paymentPayload.Currency, originalInvoice.Total.Currency.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteCurrencyMismatch",
                $"Refund currency {paymentPayload.Currency} does not match invoice currency {originalInvoice.Total.Currency.Name}.");
        }

        // V1 only supports full-amount refunds (PartialRefund / Adjustment are reserved for
        // v2). A non-full-amount refund is a user-shape error per BC doc § 17 — return
        // Result.Fail so the consumer warns-and-continues instead of DLT'ing the refund
        // event indefinitely.
        if (paymentPayload.RefundedAmount != originalInvoice.Total.Amount)
        {
            return Result.Fail<Guid>(InvoicingErrors.PartialRefundNotSupportedV1());
        }

        var reason = CreditNoteReason.OrderCancelled;
        var utcNow = _timeProvider.GetUtcNow();

        // ADR-0018 — the allocator demands an enclosing transaction. When dispatched from
        // the M6 consumer, the inbox middleware already owns a transaction; in that case
        // we must NOT begin a nested one. See IssueInvoiceCommandHandler for the same
        // rationale.
        var ownsTransaction = _db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        var creditNoteNumber = await _numberAllocator.AllocateAsync(ct);

        var snapshot = originalInvoice.ToReversalSnapshot(utcNow);
        var createResult = CreditNote.Create(snapshot, reason, command.CorrelationId, utcNow);
        if (createResult.IsFailed)
        {
            // Bug-class — Create's failure paths are all post-validation contract checks.
            throw new DataIntegrityException(
                "Invoicing.CreditNoteFactoryFailed",
                string.Join("; ", createResult.Errors.Select(e => e.Message)));
        }

        var creditNote = createResult.Value;

        // Stamp number first so the renderer can emit it (chicken-and-egg note in CreditNote.AssignCreditNoteNumber).
        creditNote.AssignCreditNoteNumber(creditNoteNumber);

        var pdfResult = await _pdfGenerator.GenerateCreditNoteAsync(creditNote, ct);

        var blobName = InvoicePdfBlobName.For(creditNoteNumber);
        var pdfBlobRef = await _blobStore.UploadAsync(
            _blobOptions.InvoicesContainerName,
            blobName,
            pdfResult.Content,
            PdfContentType,
            metadata: null,
            ct);

        var issueResult = creditNote.Issue(pdfBlobRef, utcNow);
        if (issueResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteIssueFailed",
                string.Join("; ", issueResult.Errors.Select(e => e.Message)));
        }

        // Cancel the original invoice — emits InvoiceCancelledDomainEvent which fans out to
        // InvoiceCancelledEvent on the outbox alongside CreditNoteIssuedEvent. Both events
        // carry the same CorrelationId so downstream consumers can correlate them.
        var cancelResult = originalInvoice.Cancel(creditNote.Id, reason, utcNow);
        if (cancelResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Invoicing.InvoiceCancelFailed",
                string.Join("; ", cancelResult.Errors.Select(e => e.Message)));
        }

        _db.CreditNotes.Add(creditNote);
        pending.IssuedCreditNoteId = creditNote.Id;

        await _db.SaveChangesAsync(ct);

        if (ownsTransaction)
        {
            await transaction!.CommitAsync(ct);
        }

        _logger.LogInformation(
            "Issued credit note {CreditNoteNumber} ({CreditNoteId}) reversing invoice {OriginalInvoiceId} for CorrelationId {CorrelationId}; total={Total} {Currency}.",
            creditNoteNumber.Value,
            creditNote.Id,
            originalInvoice.Id,
            command.CorrelationId,
            creditNote.Total.Amount.ToString(CultureInfo.InvariantCulture),
            creditNote.Total.Currency.Name);

        return Result.Ok(creditNote.Id);
    }

    private static OrderPayload DeserializeOrderPayload(string json, Guid correlationId)
    {
        try
        {
            return JsonSerializer.Deserialize<OrderPayload>(json, JsonOptions.Default)
                ?? throw new DataIntegrityException(
                    "Invoicing.PendingCreditNoteEmptyOrderPayload",
                    $"OrderCancelledEvent payload for {correlationId} was empty.");
        }
        catch (JsonException ex)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingCreditNoteCorruptOrderPayload",
                $"OrderCancelledEvent payload for {correlationId} is not valid JSON: {ex.Message}");
        }
    }

    private static PaymentPayload DeserializePaymentPayload(string json, Guid correlationId)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PaymentPayload>(json, JsonOptions.Default);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Currency))
            {
                throw new DataIntegrityException(
                    "Invoicing.PendingCreditNotePaymentPayloadIncomplete",
                    $"PaymentRefundedEvent payload for {correlationId} is incomplete.");
            }

            return payload;
        }
        catch (JsonException ex)
        {
            throw new DataIntegrityException(
                "Invoicing.PendingCreditNoteCorruptPaymentPayload",
                $"PaymentRefundedEvent payload for {correlationId} is not valid JSON: {ex.Message}");
        }
    }

    private static class JsonOptions
    {
        internal static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    private sealed record OrderPayload
    {
        public required Guid OrderId { get; init; }

        public required Guid CorrelationId { get; init; }

        public required Guid BuyerId { get; init; }

        public string? Reason { get; init; }
    }

    /// <summary>Mirrors the JSON shape emitted by <c>PaymentRefundedCreditNoteProjectionKafkaHandler.SerializePayload</c>.</summary>
    /// <remarks>
    /// The producer-side handler (M6) writes <see cref="RefundedAmount"/> as the absolute
    /// refunded amount (positive) — the credit note's domain layer flips the sign during
    /// snapshot construction inside <c>Invoice.ToReversalSnapshot</c>.
    /// </remarks>
    private sealed record PaymentPayload
    {
        public required Guid CorrelationId { get; init; }

        public Guid? UserId { get; init; }

        public required Guid PaymentTransactionId { get; init; }

        public Guid? RefundTransactionId { get; init; }

        public required decimal RefundedAmount { get; init; }

        public required string Currency { get; init; }

        public DateTime? RefundedAtUtc { get; init; }
    }
}
