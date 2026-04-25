using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Microsoft.EntityFrameworkCore;
using SmartEnum.EFCore;

namespace Invoicing.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Invoicing bounded context. Implements the
/// <see cref="IInvoicingDbContext"/> Application port. M5 owns the two
/// number-allocator tables (ADR-0018) only; M6 adds the pending-projection +
/// outbox/inbox tables; M7 adds the <c>Invoice</c> + <c>CreditNote</c>
/// aggregate sets so issuance can persist atomically with the allocator
/// increment.
/// </summary>
public sealed class InvoicingDbContext : DbContext, IInvoicingDbContext
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Harmless today (no SmartEnums persisted in M5) but retained so the
        // Invoice.Status / CreditNote.Status columns land cleanly when the
        // aggregate mappings arrive in M7.
        configurationBuilder.ConfigureSmartEnum();
    }
}
