using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Domain.Invoices.Events;

/// <summary>
/// In-process event \u2014 raised when an <c>Invoice</c> aggregate is first constructed (Draft).
/// Never published to Kafka directly; drives in-process logging/metrics.
/// </summary>
public sealed record InvoiceCreatedDomainEvent : DomainEvent
{
    public required Guid InvoiceId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }
}
