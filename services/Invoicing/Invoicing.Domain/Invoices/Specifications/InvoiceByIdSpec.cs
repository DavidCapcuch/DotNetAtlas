using Ardalis.Specification;

namespace Invoicing.Domain.Invoices.Specifications;

/// <summary>
/// Loads a single <see cref="Invoice"/> by its primary key, eagerly including the
/// owned <c>invoice_lines</c> + <c>invoice_vat_lines</c> collections so the read-side
/// projection can render the full document without lazy round-trips.
/// </summary>
/// <remarks>
/// Tagged with the spec class name for SQL-level observability (EF Core emits the tag
/// as a comment in the generated query).
/// </remarks>
public sealed class InvoiceByIdSpec : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByIdSpec(Guid invoiceId)
    {
        Query
            .Where(i => i.Id == invoiceId)
            .Include(i => i.Lines)
            .Include(i => i.VatLines)
            .TagWith(nameof(InvoiceByIdSpec));
    }
}
