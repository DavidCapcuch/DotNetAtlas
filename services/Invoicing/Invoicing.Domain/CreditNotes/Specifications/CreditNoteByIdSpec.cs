using Ardalis.Specification;

namespace Invoicing.Domain.CreditNotes.Specifications;

/// <summary>
/// Loads a single <see cref="CreditNote"/> by its primary key, eagerly including the
/// owned <c>credit_note_lines</c> collection so the read-side projection can render
/// the full document without lazy round-trips.
/// </summary>
public sealed class CreditNoteByIdSpec : Specification<CreditNote>, ISingleResultSpecification<CreditNote>
{
    public CreditNoteByIdSpec(Guid creditNoteId)
    {
        Query
            .Where(cn => cn.Id == creditNoteId)
            .Include(cn => cn.Lines)
            .TagWith(nameof(CreditNoteByIdSpec));
    }
}
