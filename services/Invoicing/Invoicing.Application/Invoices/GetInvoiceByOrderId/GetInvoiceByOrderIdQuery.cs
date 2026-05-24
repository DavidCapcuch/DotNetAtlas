using Invoicing.Application.Invoices.GetInvoiceById;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoiceByOrderId;

/// <summary>
/// Reads the unique invoice settling a given order. Authorization mirrors
/// <see cref="GetInvoiceById.GetInvoiceByIdQuery"/>: buyer sees only own invoice;
/// admin sees any. Cross-buyer lookups resolve to <c>NotFound</c> (existence not leaked).
/// </summary>
/// <remarks>
/// Backed by the unique index <c>UX_Invoices_OrderId</c> — there is at most one invoice per
/// order (M7 idempotency contract).
/// </remarks>
public sealed record GetInvoiceByOrderIdQuery : IQuery<GetInvoiceByIdResponse>
{
    public required Guid OrderId { get; init; }

    public required Guid BuyerId { get; init; }

    public required bool IsAdmin { get; init; }
}
