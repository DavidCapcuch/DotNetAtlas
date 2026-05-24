using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoiceById;

/// <summary>
/// Reads a single invoice by id. Authorization: buyer sees only own; admin sees any.
/// Buyer-asks-for-another's-invoice resolves to NotFound (existence not leaked) per the
/// Ordering precedent (<c>GetOrderByIdQuery</c>).
/// </summary>
public sealed record GetInvoiceByIdQuery : IQuery<GetInvoiceByIdResponse>
{
    public required Guid InvoiceId { get; init; }

    public required Guid BuyerId { get; init; }

    public required bool IsAdmin { get; init; }
}
