using Invoicing.Application.Common.Numbering;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Invoicing.Application.Common.Data;

/// <summary>
/// Application-layer port for the Invoicing persistence context (ADR-0018 +
/// ADR-0017 boundaries). M5 surfaces only the two number-allocator DbSets and
/// the transactional primitives needed by the allocator adapter; M6 adds the
/// pending-projection rows; M7 adds the outbox surface plus the
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
    /// Async-enrichment buffer: one row per <c>CorrelationId</c> assembling
    /// <c>OrderConfirmedEvent</c> + <c>PaymentCapturedEvent</c> halves until
    /// M7's <c>IssueInvoiceCommandHandler</c> consumes the converged row.
    /// </summary>
    DbSet<PendingInvoice> PendingInvoices { get; }

    /// <summary>
    /// Async-enrichment buffer for credit-note issuance: assembles
    /// <c>OrderCancelledEvent</c> + <c>PaymentRefundedEvent</c> halves.
    /// </summary>
    DbSet<PendingCreditNote> PendingCreditNotes { get; }

    /// <summary>
    /// Database facade for transaction control + raw SQL execution (the
    /// <c>SELECT ... FOR UPDATE</c> + <c>INSERT ... ON CONFLICT DO NOTHING</c>
    /// path inside the allocator adapters).
    /// </summary>
    DatabaseFacade Database { get; }

    /// <summary>Persists tracked changes within the ambient transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
