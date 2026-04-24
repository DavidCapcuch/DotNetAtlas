using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Domain.Invoices.Events;

/// <summary>
/// In-process event \u2014 raised when an <c>Invoice</c> transitions to <c>Cancelled</c> (I-6).
/// Always carries the reversing <c>CreditNoteId</c> per I-6. Drives the outbox publisher
/// that emits <c>InvoiceCancelledEvent</c>.
/// </summary>
public sealed record InvoiceCancelledDomainEvent : DomainEvent
{
    public required Guid InvoiceId { get; init; }

    public required Guid BuyerId { get; init; }

    public required DateTimeOffset CancelledAtUtc { get; init; }

    public required CreditNoteReason Reason { get; init; }

    public required Guid CreditNoteId { get; init; }

    public required Guid CorrelationId { get; init; }
}
