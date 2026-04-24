using FluentResults;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes.Events;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.CreditNotes;

/// <summary>
/// Aggregate root \u2014 credit note reversing a previously-issued <see cref="Invoice"/>.
/// Credit notes are immediately issued (no <c>Draft</c> state) and cannot be cancelled.
/// </summary>
/// <remarks>
/// Invariants ([invoicing.md \u00a7 2.2](../../../../docs/bc-design/invoicing.md) lines 84\u201387):
/// <list type="number">
/// <item><c>I-CN-1</c> \u2014 <see cref="OriginalInvoiceId"/> references an invoice in <c>Issued</c>
/// or <c>Delivered</c> state (validated by the command handler before construction).</item>
/// <item><c>I-CN-2</c> \u2014 <see cref="Total"/> is strictly negative.</item>
/// <item><c>I-CN-3</c> \u2014 <see cref="CreditNoteNumber"/> is immutable post-allocation.</item>
/// </list>
/// v1 only supports full-amount reversals for <see cref="CreditNoteReason.OrderCancelled"/>;
/// partial refunds are rejected at the command handler with
/// <c>InvoicingErrors.PartialRefundNotSupportedV1</c>.
/// </remarks>
public sealed class CreditNote : AggregateRoot<Guid>
{
    private readonly List<InvoiceLine> _lines = [];

    public CreditNoteNumber? CreditNoteNumber { get; private set; }

    public Guid OriginalInvoiceId { get; private set; }

    public InvoiceNumber OriginalInvoiceNumber { get; private set; } = default!;

    public Guid BuyerId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTimeOffset IssueDate { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public Money Total { get; private set; } = default!;

    public CreditNoteReason Reason { get; private set; } = default!;

    public PdfBlobRef? PdfBlobRef { get; private set; }

    public CreditNoteStatus Status { get; private set; } = CreditNoteStatus.Issued;

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    private CreditNote()
    {
    }

    /// <summary>
    /// Creates a credit note reversing <paramref name="originalInvoice"/>. Lines are copied
    /// from the original with flipped signs; <see cref="Total"/> is the inverse of
    /// <see cref="Invoice.Total"/>. Raises <see cref="CreditNoteCreatedDomainEvent"/>.
    /// </summary>
    /// <remarks>
    /// The aggregate transitions to <see cref="CreditNoteStatus.Issued"/> only after the
    /// allocator provides a <see cref="CreditNoteNumber"/> and the PDF is uploaded; call
    /// <see cref="Issue"/> to complete the state.
    /// </remarks>
    public static Result<CreditNote> Create(
        Invoice originalInvoice,
        CreditNoteReason reason,
        Guid correlationId,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(originalInvoice);
        ArgumentNullException.ThrowIfNull(reason);

        Throw.If(correlationId == Guid.Empty, new DataIntegrityException(
            "Invoicing.InvalidCorrelationId", "CreditNote CorrelationId must not be empty."));

        // I-CN-1 \u2014 original invoice must be in Issued or Delivered. A cancelled invoice is
        // a bug-class condition (the handler should have short-circuited with
        // CreditNoteRefersToCancelledInvoice first), so it throws rather than Result.Fail.
        if (originalInvoice.Status != InvoiceStatus.Issued && originalInvoice.Status != InvoiceStatus.Delivered)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteRefersToCancelledInvoice",
                $"Credit note cannot reference invoice in state '{originalInvoice.Status.Name}' (I-CN-1).");
        }

        if (originalInvoice.InvoiceNumber is null)
        {
            throw new DataIntegrityException(
                "Invoicing.OriginalInvoiceMissingNumber",
                "Credit note requires the original invoice to have an allocated InvoiceNumber.");
        }

        var reversedLines = originalInvoice.LinesForReversal();
        var originalTotal = originalInvoice.Total;

        // I-CN-2 \u2014 Total is negative. Construct via primary Money ctor (intent is explicit).
        var negativeTotal = new Money(-originalTotal.Amount, originalTotal.Currency);

        var creditNote = new CreditNote
        {
            Id = Guid.CreateVersion7(),
            OriginalInvoiceId = originalInvoice.Id,
            OriginalInvoiceNumber = originalInvoice.InvoiceNumber,
            BuyerId = originalInvoice.BuyerId,
            CorrelationId = correlationId,
            Total = negativeTotal,
            Reason = reason,
            Status = CreditNoteStatus.Issued,
        };

        creditNote._lines.AddRange(reversedLines);

        creditNote.AddDomainEvent(new CreditNoteCreatedDomainEvent
        {
            CreditNoteId = creditNote.Id,
            OriginalInvoiceId = originalInvoice.Id,
            CorrelationId = correlationId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok(creditNote);
    }

    /// <summary>
    /// Stamps the gap-free <see cref="CreditNoteNumber"/> (ADR-0018) and the stored PDF
    /// reference, confirming the <c>Issued</c> state. Raises
    /// <see cref="CreditNoteIssuedDomainEvent"/>.
    /// </summary>
    public Result Issue(CreditNoteNumber creditNoteNumber, PdfBlobRef pdfBlobRef, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(creditNoteNumber);
        ArgumentNullException.ThrowIfNull(pdfBlobRef);

        // Only permit the stamp when the aggregate has not yet been fully issued.
        if (CreditNoteNumber is not null || PdfBlobRef is not null)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteAlreadyIssued",
                "CreditNote has already been issued (number + PDF stamped).");
        }

        CreditNoteNumber = creditNoteNumber;
        PdfBlobRef = pdfBlobRef;
        IssueDate = utcNow;

        AddDomainEvent(new CreditNoteIssuedDomainEvent
        {
            CreditNoteId = Id,
            CreditNoteNumber = creditNoteNumber,
            OriginalInvoiceId = OriginalInvoiceId,
            OriginalInvoiceNumber = OriginalInvoiceNumber,
            BuyerId = BuyerId,
            CorrelationId = CorrelationId,
            IssueDate = utcNow,
            Total = Total,
            Reason = Reason,
            PdfBlobRef = pdfBlobRef,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions <c>Issued \u2192 Delivered</c>. Credit notes use the same delivery channels
    /// as invoices (currently email only in v1). Requires <see cref="Issue"/> to have stamped
    /// the number + PDF first \u2014 a credit note without these artifacts has nothing to deliver.
    /// </summary>
    public Result Deliver(DateTimeOffset deliveredAtUtc)
    {
        if (CreditNoteNumber is null || PdfBlobRef is null)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteNotIssued",
                "CreditNote cannot be delivered before Issue stamps the number and PDF.");
        }

        var transition = Status.CanTransitionTo(CreditNoteStatus.Delivered);
        if (transition.IsFailed)
        {
            return transition;
        }

        Status = CreditNoteStatus.Delivered;
        DeliveredAtUtc = deliveredAtUtc;
        return Result.Ok();
    }

    /// <summary>
    /// Transitions <c>Delivered \u2192 Archived</c>.
    /// </summary>
    public Result Archive()
    {
        var transition = Status.CanTransitionTo(CreditNoteStatus.Archived);
        if (transition.IsFailed)
        {
            return transition;
        }

        Status = CreditNoteStatus.Archived;
        return Result.Ok();
    }
}
