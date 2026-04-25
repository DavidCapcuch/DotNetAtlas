using Invoicing.Application.Common.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Invoicing.Application.Common.Data;

/// <summary>
/// Application-layer port for the Invoicing persistence context (ADR-0018 +
/// ADR-0017 boundaries). M5 surfaces only the two number-allocator DbSets and
/// the transactional primitives needed by the allocator adapter; M6 adds the
/// pending-projection rows + outbox via <c>IOutboxDbContext</c>; M7 adds the
/// <c>Invoice</c> + <c>CreditNote</c> aggregate sets.
/// </summary>
/// <remarks>
/// Each milestone owns its slice of this surface — the interface grows as the
/// command and projection handlers that need it land. Keeping it minimal in M5
/// prevents the allocator adapter from depending on persistence shapes that
/// haven't been designed yet.
/// </remarks>
public interface IInvoicingDbContext
{
    /// <summary>One row per fiscal year holding the next invoice sequence.</summary>
    DbSet<InvoiceNumberAllocator> InvoiceNumberAllocators { get; }

    /// <summary>One row per fiscal year holding the next credit-note sequence.</summary>
    DbSet<CreditNoteNumberAllocator> CreditNoteNumberAllocators { get; }

    /// <summary>
    /// Database facade for transaction control + raw SQL execution (the
    /// <c>SELECT ... FOR UPDATE</c> + <c>INSERT ... ON CONFLICT DO NOTHING</c>
    /// path inside the allocator adapters).
    /// </summary>
    DatabaseFacade Database { get; }

    /// <summary>Persists tracked changes within the ambient transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
