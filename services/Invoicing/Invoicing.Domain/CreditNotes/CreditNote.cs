using FluentResults;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes.Events;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.CreditNotes;

/// <summary>
/// Aggregate root \u2014 credit note reversing a previously-issued
/// <c>Invoicing.Domain.Invoices.Invoice</c>. Credit notes are immediately issued (no
/// <c>Draft</c> state) and cannot be cancelled.
/// </summary>
/// <remarks>
/// Invariants ([invoicing.md \u00a7 2.2](../../../../docs/bc-design/invoicing.md) lines 84\u201387):
/// <list type="number">
/// <item><c>I-CN-1</c> \u2014 <see cref="OriginalInvoiceId"/> references an invoice in <c>Issued</c>
/// or <c>Delivered</c> state (gated at the command handler; the snapshot factory on
/// <c>Invoice.ToReversalSnapshot</c> defensively re-asserts the same precondition as a
/// bug-class guard).</item>
/// <item><c>I-CN-2</c> \u2014 <see cref="Total"/> is strictly negative.</item>
/// <item><c>I-CN-3</c> \u2014 <see cref="CreditNoteNumber"/> is immutable post-allocation.</item>
/// </list>
/// v1 only supports full-amount reversals for <see cref="CreditNoteReason.OrderCancelled"/>;
/// partial refunds are rejected at the command handler with
/// <c>InvoicingErrors.PartialRefundNotSupportedV1</c>.
/// CreditNote receives its source Invoice's state via <see cref="InvoiceSnapshot"/> so the
/// aggregate boundary stays clean (DDD: references other aggregates by Id only).
/// </remarks>
public sealed class CreditNote : AggregateRoot<Guid>
{
    private readonly List<CreditNoteLine> _lines = [];

    public CreditNoteNumber? CreditNoteNumber { get; private set; }

    public Guid OriginalInvoiceId { get; private set; }

    public InvoiceNumber OriginalInvoiceNumber { get; private set; } = default!;

    public Guid BuyerId { get; private set; }

    public DateTimeOffset IssueDate { get; private set; }

    public IReadOnlyList<CreditNoteLine> Lines => _lines;

    public Money Total { get; private set; } = default!;

    public CreditNoteReason Reason { get; private set; } = default!;

    public PdfBlobRef? PdfBlobRef { get; private set; }

    public CreditNoteStatus Status { get; private set; } = CreditNoteStatus.Issued;

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    private CreditNote()
    {
    }

    /// <summary>
    /// Creates a credit note reversing the invoice captured by
    /// <paramref name="originalInvoiceSnapshot"/>. The snapshot carries pre-flipped lines and
    /// the source total; this factory only computes the negative total and stores. Raises
    /// <see cref="CreditNoteCreatedDomainEvent"/>.
    /// </summary>
    /// <remarks>
    /// Eligibility (status Issued/Delivered + InvoiceNumber allocated) is guaranteed by
    /// <c>Invoice.ToReversalSnapshot</c> at snapshot time; this factory does not re-check.
    /// The aggregate transitions to <see cref="CreditNoteStatus.Issued"/> only after the
    /// allocator provides a <see cref="CreditNoteNumber"/> and the PDF is uploaded; call
    /// <see cref="Issue(CreditNoteNumber, PdfBlobRef, DateTimeOffset)"/> to complete the state.
    /// </remarks>
    public static Result<CreditNote> Create(
        InvoiceSnapshot originalInvoiceSnapshot,
        CreditNoteReason reason,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(originalInvoiceSnapshot);
        ArgumentNullException.ThrowIfNull(reason);

        // I-CN-2 \u2014 Total is negative. Negate the (positive) source invoice total.
        var negativeTotal = originalInvoiceSnapshot.Total.Negate();

        // Defense-in-depth: source invoice already enforces Total >= 0 at issuance, so
        // negate produces a strictly-negative value. Untestable through valid call paths;
        // documents I-CN-2, which Money's sign-neutrality leaves to this type (rejects both zero and
        // positive \u2014 zero-total credit notes have no business meaning).
        if (negativeTotal.Amount >= 0)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteTotalNotNegative",
                $"CreditNote total must be strictly negative (I-CN-2); was {negativeTotal.Amount} {negativeTotal.Currency.Name}. " +
                $"Source invoice total was {originalInvoiceSnapshot.Total.Amount}.");
        }

        var creditNote = new CreditNote
        {
            Id = Guid.CreateVersion7(),
            OriginalInvoiceId = originalInvoiceSnapshot.InvoiceId,
            OriginalInvoiceNumber = originalInvoiceSnapshot.InvoiceNumber,
            BuyerId = originalInvoiceSnapshot.BuyerId,
            Total = negativeTotal,
            Reason = reason,
            Status = CreditNoteStatus.Issued,
        };

        creditNote._lines.AddRange(originalInvoiceSnapshot.ReversalLines);

        creditNote.AddDomainEvent(new CreditNoteCreatedDomainEvent
        {
            CreditNoteId = creditNote.Id,
            OriginalInvoiceId = originalInvoiceSnapshot.InvoiceId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok(creditNote);
    }

    /// <summary>
    /// Stamps the gap-free <see cref="CreditNoteNumber"/> on a credit note that has not
    /// yet been issued, without raising domain events. Used by the command handler so
    /// the PDF renderer can include the number on its first pass — the
    /// <see cref="PdfBlobRef"/> only lands after upload, but the renderer needs the number
    /// embedded in the document. The number is immutable post-allocation per I-CN-3.
    /// </summary>
    public void AssignCreditNoteNumber(CreditNoteNumber creditNoteNumber)
    {
        ArgumentNullException.ThrowIfNull(creditNoteNumber);

        if (CreditNoteNumber is not null)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteNumberAlreadyAssigned",
                "CreditNoteNumber is immutable once assigned (I-CN-3).");
        }

        if (PdfBlobRef is not null)
        {
            // PdfBlobRef set without a number is a corrupted construction — the handler
            // should always assign the number first. Surface as bug-class.
            throw new DataIntegrityException(
                "Invoicing.CreditNoteAlreadyIssued",
                "CreditNote already has a PDF stamped; cannot retrofit a number.");
        }

        CreditNoteNumber = creditNoteNumber;
    }

    /// <summary>
    /// Stamps the gap-free <see cref="CreditNoteNumber"/> (ADR-0018) and the stored PDF
    /// reference, confirming the <c>Issued</c> state. Raises
    /// <see cref="CreditNoteIssuedDomainEvent"/>.
    /// </summary>
    /// <remarks>
    /// Convenience overload that composes <see cref="AssignCreditNoteNumber"/> + the
    /// no-number <see cref="Issue(PdfBlobRef, DateTimeOffset)"/>. Use the split form in
    /// the command handler when the PDF must render with the number embedded.
    /// </remarks>
    public Result Issue(CreditNoteNumber creditNoteNumber, PdfBlobRef pdfBlobRef, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(creditNoteNumber);
        ArgumentNullException.ThrowIfNull(pdfBlobRef);

        // If the aggregate already has a stamped PDF, the issuance is complete — reject
        // before any further mutation, preserving I-CN-3 (number immutable) + the
        // write-once PdfBlobRef contract.
        if (PdfBlobRef is not null)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteAlreadyIssued",
                "CreditNote has already been issued (number + PDF stamped).");
        }

        if (CreditNoteNumber is null)
        {
            AssignCreditNoteNumber(creditNoteNumber);
        }
        else if (!CreditNoteNumber.Equals(creditNoteNumber))
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteNumberMismatchOnIssue",
                "CreditNoteNumber passed to Issue does not match the previously-assigned number (I-CN-3).");
        }

        return Issue(pdfBlobRef, utcNow);
    }

    /// <summary>
    /// Stamps the PDF reference using the previously-assigned
    /// <see cref="CreditNoteNumber"/>. Raises <see cref="CreditNoteIssuedDomainEvent"/>.
    /// Requires <see cref="AssignCreditNoteNumber"/> to have been called.
    /// </summary>
    public Result Issue(PdfBlobRef pdfBlobRef, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(pdfBlobRef);

        if (CreditNoteNumber is null)
        {
            throw new DataIntegrityException(
                "Invoicing.IssueWithoutCreditNoteNumber",
                "Issue requires a CreditNoteNumber — call AssignCreditNoteNumber first.");
        }

        if (PdfBlobRef is not null)
        {
            throw new DataIntegrityException(
                "Invoicing.CreditNoteAlreadyIssued",
                "CreditNote has already been issued (PDF stamped).");
        }

        PdfBlobRef = pdfBlobRef;
        IssueDate = utcNow;

        AddDomainEvent(new CreditNoteIssuedDomainEvent
        {
            CreditNoteId = Id,
            CreditNoteNumber = CreditNoteNumber,
            OriginalInvoiceId = OriginalInvoiceId,
            OriginalInvoiceNumber = OriginalInvoiceNumber,
            BuyerId = BuyerId,
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
