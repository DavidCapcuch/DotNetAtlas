using Invoicing.Domain.Invoices.Events;
using Invoicing.Invoices;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Maps <see cref="InvoiceCancelledDomainEvent"/> to the external Avro
/// <see cref="InvoiceCancelledEvent"/>. No decimal fields — the cancellation event is a
/// pure status transition with cross-references (<c>CreditNoteId</c>) to let consumers
/// correlate with the matching <c>CreditNoteIssuedEvent</c> on the same correlation id.
/// </summary>
internal static class InvoiceCancelledMapper
{
    public static InvoiceCancelledEvent ToInvoiceCancelledEvent(this InvoiceCancelledDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new InvoiceCancelledEvent
        {
            InvoiceId = source.InvoiceId,
            BuyerId = source.BuyerId,
            CancelledAtUtc = source.CancelledAtUtc.UtcDateTime,
            Reason = source.Reason.Name,
            CreditNoteId = source.CreditNoteId,
            CorrelationId = source.CorrelationId,
        };
    }
}
