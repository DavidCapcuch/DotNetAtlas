using Ardalis.Specification;

namespace Invoicing.Domain.Invoices.Specifications;

/// <summary>
/// Lists invoices for a given buyer with offset / limit paging, most-recent-first.
/// Includes owned line collections so the projection can return a complete document
/// summary per row without N+1 round-trips.
/// </summary>
/// <remarks>
/// Keyset paging is out of scope for v1 (use-cases.md § Conventions) — Skip / Take
/// is fine at expected per-buyer invoice counts. A keyset successor may replace this
/// spec in a later milestone.
/// </remarks>
public sealed class InvoicesByBuyerSpec : Specification<Invoice>
{
    public InvoicesByBuyerSpec(Guid buyerId, int skip, int take)
    {
        Query
            .Where(i => i.BuyerId == buyerId)
            .Include(i => i.Lines)
            .Include(i => i.VatLines)
            // Deterministic paging: primary by issue recency, tie-broken by Id
            // (Guid v7 — time-ordered) so two invoices with equal IssueDate at
            // sub-ms resolution never drop or duplicate across pages.
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.Id)
            .Skip(skip)
            .Take(take)
            .TagWith(nameof(InvoicesByBuyerSpec));
    }
}
