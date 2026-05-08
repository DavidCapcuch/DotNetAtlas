using Invoicing.Application.Common.Numbering;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Invoicing.Application.Common.Data;

/// <summary>
/// Application-layer port for the Invoicing persistence context (ADR-0018 +
/// ADR-0017 boundaries). M5 surfaced only the two number-allocator DbSets and
/// the transactional primitives needed by the allocator adapter; M6 added the
/// pending-projection rows; M7 adds the <c>Invoice</c> + <c>CreditNote</c>
/// aggregate sets so the issuance command handlers can persist atomically with
/// the allocator increment + outbox row.
/// </summary>
/// <remarks>
/// Each milestone owns its slice of this surface — the interface grows as the
/// command and projection handlers that need it land. Keeping it minimal in M5
/// prevented the allocator adapter from depending on persistence shapes that
/// hadn't been designed yet; M7 finalises it for the inside-transaction work
/// performed by <c>IssueInvoiceCommandHandler</c> and
/// <c>IssueCreditNoteCommandHandler</c>.
/// </remarks>
public interface IInvoicingDbContext : IOutboxDbContext
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
    /// Issued <see cref="Invoice"/> aggregates. Written by
    /// <c>IssueInvoiceCommandHandler</c> and updated by the credit-note path
    /// when <c>Invoice.Cancel(...)</c> records the reversing CreditNoteId.
    /// </summary>
    DbSet<Invoice> Invoices { get; }

    /// <summary>
    /// Issued <see cref="CreditNote"/> aggregates. Written by
    /// <c>IssueCreditNoteCommandHandler</c> in the same transaction as the
    /// reversing <see cref="Invoice"/> mutation.
    /// </summary>
    DbSet<CreditNote> CreditNotes { get; }

    // Note: Database (DatabaseFacade) and SaveChangesAsync(CancellationToken) are
    // inherited from IOutboxDbContext — the M5 allocator code path uses Database to
    // own the BEGIN/COMMIT for FOR UPDATE row locks, and the M7 command handlers
    // call SaveChangesAsync to commit the aggregate + outbox row atomically.
}
