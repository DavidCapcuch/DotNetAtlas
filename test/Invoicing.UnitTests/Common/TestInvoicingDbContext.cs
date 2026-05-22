using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.ReliableMessaging.Outbox.Core;

namespace Invoicing.UnitTests.Common;

/// <summary>
/// Minimal test-only EF Core in-memory implementation of <see cref="IInvoicingDbContext"/>
/// used by unit tests that need to exercise handlers which query the <see cref="Invoices"/>
/// set. Complex SmartEnum conversions, owned-type mappings, and column configurations are
/// intentionally omitted — EF InMemory stores the tracked CLR objects by reference, so
/// LINQ queries against aggregate properties work without full production configuration.
/// Transactional semantics are not emulated; that is integration-test territory.
/// </summary>
public sealed class TestInvoicingDbContext : DbContext, IInvoicingDbContext
{
    public TestInvoicingDbContext(DbContextOptions<TestInvoicingDbContext> options)
        : base(options)
    {
    }

    public DbSet<InvoiceNumberAllocator> InvoiceNumberAllocators => Set<InvoiceNumberAllocator>();

    public DbSet<CreditNoteNumberAllocator> CreditNoteNumberAllocators => Set<CreditNoteNumberAllocator>();

    public DbSet<PendingInvoice> PendingInvoices => Set<PendingInvoice>();

    public DbSet<PendingCreditNote> PendingCreditNotes => Set<PendingCreditNote>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public static TestInvoicingDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestInvoicingDbContext>()
            .UseInMemoryDatabase(Guid.CreateVersion7().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestInvoicingDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Minimal mapping — just primary keys so EF InMemory can track entities.
        // SmartEnum conversions, OwnsOne VOs, and column names are not needed for
        // in-process LINQ queries; the production InvoicingDbContext in Infrastructure
        // owns those concerns. InMemory keeps live CLR references, so property access
        // works as-is on the tracked aggregate objects.
        modelBuilder.Entity<Invoice>(b =>
        {
            b.HasKey(i => i.Id);
            // Ignore complex VO / SmartEnum / owned-type properties.
            // EF InMemory stores the live CLR reference so these are still accessible
            // on the tracked entity after Add + query.
            b.Ignore(i => i.InvoiceNumber);
            b.Ignore(i => i.BillingAddress);
            b.Ignore(i => i.Lines);
            b.Ignore(i => i.VatLines);
            b.Ignore(i => i.Subtotal);
            b.Ignore(i => i.Total);
            b.Ignore(i => i.PdfBlobRef);
            b.Ignore(i => i.DeliveryChannel);
            b.Ignore(i => i.Status);
            b.Ignore(i => i.CancellationInfo);
        });

        modelBuilder.Entity<CreditNote>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.CreditNoteNumber);
            b.Ignore(c => c.OriginalInvoiceNumber);
            b.Ignore(c => c.Lines);
            b.Ignore(c => c.Total);
            b.Ignore(c => c.Reason);
            b.Ignore(c => c.PdfBlobRef);
            b.Ignore(c => c.Status);
        });

        modelBuilder.Entity<InvoiceNumberAllocator>(b => b.HasKey(a => a.Year));
        modelBuilder.Entity<CreditNoteNumberAllocator>(b => b.HasKey(a => a.Year));
        modelBuilder.Entity<PendingInvoice>(b => b.HasKey(p => p.CorrelationId));
        modelBuilder.Entity<PendingCreditNote>(b => b.HasKey(p => p.CorrelationId));
        modelBuilder.Entity<OutboxMessage>(b => b.HasKey(m => m.Id));
    }
}
