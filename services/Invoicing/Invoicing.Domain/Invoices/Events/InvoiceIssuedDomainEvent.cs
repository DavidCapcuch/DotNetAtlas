using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Invoices.Events;

/// <summary>
/// In-process event \u2014 raised when an <c>Invoice</c> transitions <c>Draft \u2192 Issued</c> (number
/// allocated, PDF generated + stored). Drives the outbox publisher that emits
/// <c>InvoiceIssuedEvent</c> on <c>invoicing.invoices</c>. All data needed to build the external
/// Avro event travels on this record so the publisher never reloads the aggregate.
/// </summary>
public sealed record InvoiceIssuedDomainEvent : DomainEvent
{
    public required Guid InvoiceId { get; init; }

    public required InvoiceNumber InvoiceNumber { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required Guid PaymentId { get; init; }

    public required DateTimeOffset IssueDate { get; init; }

    public required Address BillingAddress { get; init; }

    public required Money Subtotal { get; init; }

    public required Money Total { get; init; }

    public required IReadOnlyList<VatLine> VatLines { get; init; }

    public required PdfBlobRef PdfBlobRef { get; init; }

    public required DeliveryChannel DeliveryChannel { get; init; }
}
