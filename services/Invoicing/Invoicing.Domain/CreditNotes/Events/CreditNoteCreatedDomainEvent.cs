using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Domain.CreditNotes.Events;

/// <summary>
/// In-process event \u2014 raised when a <c>CreditNote</c> aggregate is first constructed.
/// Credit notes are immediately issued (no <c>Draft</c> state), so this event is followed
/// closely by <see cref="CreditNoteIssuedDomainEvent"/>.
/// </summary>
public sealed record CreditNoteCreatedDomainEvent : DomainEvent
{
    public required Guid CreditNoteId { get; init; }

    public required Guid OriginalInvoiceId { get; init; }

    public required Guid CorrelationId { get; init; }
}
