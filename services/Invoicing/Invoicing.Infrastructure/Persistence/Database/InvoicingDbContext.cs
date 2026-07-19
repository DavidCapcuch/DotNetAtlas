using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using SmartEnum.EFCore;

namespace Invoicing.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Invoicing bounded context. Implements
/// <see cref="IInvoicingDbContext"/> (Application port),
/// <see cref="IInboxDbContext"/> (Platform inbox-dedup port — required by
/// the KafkaFlow inbox middleware that fronts the enrichment consumers), and
/// <see cref="IOutboxDbContext"/> so the issuance command handlers can write
/// the aggregate + outbox row in one transaction via the
/// <c>DispatchDomainEventsInterceptor</c>. Owns the two number-allocator
/// tables (ADR-0018), the <see cref="PendingInvoice"/> +
/// <see cref="PendingCreditNote"/> projection tables, the
/// <c>inbox_messages</c> dedup table, the <c>Invoice</c> + <c>CreditNote</c>
/// aggregate sets, and the <c>outbox_messages</c> table so issuance persists
/// atomically with the allocator increment and external-event publication.
/// </summary>
public sealed class InvoicingDbContext : DbContext, IInvoicingDbContext, IInboxDbContext, IOutboxDbContext
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
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <inheritdoc />
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    /// <inheritdoc />
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        // The inbox + outbox tables are configured by the platform — their schema/columns must not
        // drift even if a future Invoicing-side EF refactor sweeps the assembly.
        modelBuilder.ConfigureInbox(DefaultSchemaName);
        modelBuilder.ConfigureOutbox(DefaultSchemaName);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Load-bearing: Invoice.Status / DeliveryChannel and CreditNote.Status / Reason
        // columns rely on the SmartEnum<T> conversion the convention installs.
        configurationBuilder.ConfigureSmartEnum();
    }
}
