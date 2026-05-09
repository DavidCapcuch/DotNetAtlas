using Ardalis.Specification;

namespace Invoicing.Domain.Invoices.Specifications;

/// <summary>
/// Loads a single <see cref="Invoice"/> by the <see cref="Invoice.OrderId"/> it settles.
/// Backed by the unique index <c>UX_Invoices_OrderId</c> — at most one invoice per order
/// (M7 idempotency contract on <c>IssueInvoiceCommand</c>).
/// </summary>
public sealed class InvoiceByOrderIdSpec : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByOrderIdSpec(Guid orderId)
    {
        Query
            .Where(i => i.OrderId == orderId)
            .Include(i => i.Lines)
            .Include(i => i.VatLines)
            .TagWith(nameof(InvoiceByOrderIdSpec));
    }
}
