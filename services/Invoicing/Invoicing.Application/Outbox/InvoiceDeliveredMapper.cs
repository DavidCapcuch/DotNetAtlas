using Invoicing.Domain.Invoices.Events;
using Invoicing.Invoices;
using Riok.Mapperly.Abstractions;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Maps <see cref="InvoiceDeliveredDomainEvent"/> to the external Avro
/// <see cref="InvoiceDeliveredEvent"/>.
/// </summary>
[Mapper]
public static partial class InvoiceDeliveredMapper
{
    public static InvoiceDeliveredEvent ToInvoiceDeliveredEvent(this InvoiceDeliveredDomainEvent source) =>
        new()
        {
            InvoiceId = source.InvoiceId,
            BuyerId = source.BuyerId,
            DeliveredAtUtc = source.DeliveredAtUtc.UtcDateTime,
            Channel = source.Channel.Name,
            CorrelationId = source.CorrelationId,
            OccurredOnUtc = source.OccurredOnUtc.UtcDateTime,
        };
}
