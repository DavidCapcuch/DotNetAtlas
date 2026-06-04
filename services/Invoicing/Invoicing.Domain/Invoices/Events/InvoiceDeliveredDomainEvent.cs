using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Domain.Invoices.Events;

/// <summary>
/// In-process event \u2014 raised when a delivery confirmation arrives (email accepted,
/// webhook 2xx). Drives the outbox publisher that emits <c>InvoiceDeliveredEvent</c>.
/// </summary>
public sealed record InvoiceDeliveredDomainEvent : DomainEvent
{
    public required Guid InvoiceId { get; init; }

    public required Guid BuyerId { get; init; }

    public required DateTimeOffset DeliveredAtUtc { get; init; }

    public required DeliveryChannel Channel { get; init; }
}
