using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.Projections;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using SmartEnum.EFCore;

namespace Invoicing.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Invoicing bounded context. Implements both
/// <see cref="IInvoicingDbContext"/> (Application port) and
/// <see cref="IInboxDbContext"/> (Platform inbox-dedup port — required by
/// the KafkaFlow inbox middleware that fronts the M6 enrichment consumers).
/// M5 owns the two number-allocator tables (ADR-0018); M6 adds the
/// <see cref="PendingInvoice"/> + <see cref="PendingCreditNote"/> projection
/// tables plus the <c>inbox_messages</c> dedup table; M7 adds the
/// <c>Invoice</c> + <c>CreditNote</c> aggregate sets so issuance can persist
/// atomically with the allocator increment.
/// </summary>
public sealed class InvoicingDbContext : DbContext, IInvoicingDbContext, IInboxDbContext
{
    /// <summary>Default Postgres schema for all Invoicing tables.</summary>
    public const string DefaultSchemaName = "invoicing";

    public InvoicingDbContext(DbContextOptions<InvoicingDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<InvoiceNumberAllocator> InvoiceNumberAllocators => Set<InvoiceNumberAllocator>();

    /// <inheritdoc />
    public DbSet<CreditNoteNumberAllocator> CreditNoteNumberAllocators => Set<CreditNoteNumberAllocator>();

    /// <inheritdoc />
    public DbSet<PendingInvoice> PendingInvoices => Set<PendingInvoice>();

    /// <inheritdoc />
    public DbSet<PendingCreditNote> PendingCreditNotes => Set<PendingCreditNote>();

    /// <inheritdoc />
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        // The inbox table is configured by the platform — its schema/columns must not drift
        // even if a future Invoicing-side EF refactor sweeps the assembly.
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Harmless today (no SmartEnums persisted in M5/M6) but retained so the
        // Invoice.Status / CreditNote.Status columns land cleanly when the
        // aggregate mappings arrive in M7.
        configurationBuilder.ConfigureSmartEnum();
    }
}
