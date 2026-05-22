using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Invoices.Events;

/// <summary>
/// In-process event \u2014 raised alongside <see cref="InvoiceIssuedDomainEvent"/> to trigger
/// the delivery-attempt-1 outbox row (<c>SendEmailNotificationCommand</c>) per
/// <c>example-mapping/invoicing.md \u00a7 4</c>. No external Avro event corresponds to this
/// \u2014 it's consumed purely in-process to produce the Notifications command.
/// </summary>
/// <remarks>
/// Carries <see cref="InvoiceNumber"/> and <see cref="Total"/> so the outbox publisher handler
/// does not need to re-query the DB for template data. The interceptor dispatches domain events
/// inside <c>SavingChangesAsync</c>, before the aggregate row is committed, so a DB round-trip
/// inside the handler would fail to find the invoice (see D2 wire-up notes).
/// </remarks>
public sealed record InvoiceDeliveryRequestedDomainEvent : DomainEvent
{
    public required Guid InvoiceId { get; init; }

    public required Guid BuyerId { get; init; }

    public required DeliveryChannel Channel { get; init; }

    /// <summary>Attempt number in the <c>invoice_delivery_log</c>. 1 for automatic issuance delivery.</summary>
    public required int Attempt { get; init; }

    public required Guid CorrelationId { get; init; }

    /// <summary>Allocated invoice number \u2014 used by the email template without a DB round-trip.</summary>
    public required InvoiceNumber InvoiceNumber { get; init; }

    /// <summary>Invoice total \u2014 used by the email template without a DB round-trip.</summary>
    public required Money Total { get; init; }
}
