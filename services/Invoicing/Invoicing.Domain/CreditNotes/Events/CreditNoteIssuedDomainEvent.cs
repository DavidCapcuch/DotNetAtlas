using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.CreditNotes.Events;

/// <summary>
/// In-process event \u2014 raised when a <c>CreditNote</c> is issued (number allocated, PDF stored).
/// Drives the outbox publisher that emits <c>CreditNoteIssuedEvent</c> on <c>invoicing.invoices</c>.
/// All data needed for the external Avro event is carried inline.
/// </summary>
public sealed record CreditNoteIssuedDomainEvent : DomainEvent
{
    public required Guid CreditNoteId { get; init; }

    public required CreditNoteNumber CreditNoteNumber { get; init; }

    public required Guid OriginalInvoiceId { get; init; }

    public required InvoiceNumber OriginalInvoiceNumber { get; init; }

    public required Guid BuyerId { get; init; }

    public required DateTimeOffset IssueDate { get; init; }

    /// <summary>Negative monetary total (the reversal).</summary>
    public required Money Total { get; init; }

    public required CreditNoteReason Reason { get; init; }

    public required PdfBlobRef PdfBlobRef { get; init; }
}
