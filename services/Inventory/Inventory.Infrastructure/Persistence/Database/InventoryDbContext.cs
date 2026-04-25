using Inventory.Application.Common.Data;
using Inventory.Application.Common.ReadModels;
using Inventory.Infrastructure.Persistence.EventStore;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using SmartEnum.EFCore;

namespace Inventory.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Inventory bounded context. Implements the
/// <see cref="IInventoryDbContext"/> application port and the <see cref="IInboxDbContext"/>
/// interface so that saga-command consumers (M5) and the transactional outbox
/// can share the same EF scope + transaction as the event-store append.
/// </summary>
/// <remarks>
/// Owns the append-only ES write model (<c>stock_events</c>) alongside the two
/// read projections (<c>current_stock_levels</c>, <c>reservation_audit</c>)
/// plus the platform-provided <c>outbox_message</c> + <c>inbox_message</c>
/// tables. One <c>SaveChangesAsync</c> commits all of them atomically per the
/// transactional envelope described in <c>docs/bc-design/inventory.md</c> § 8.1.
/// </remarks>
public sealed class InventoryDbContext : DbContext, IInventoryDbContext, IInboxDbContext
{
    /// <summary>Default Postgres schema for all Inventory tables.</summary>
    public const string DefaultSchemaName = "inventory";

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    /// <summary>Append-only ES stream — <c>inventory.stock_events</c>.</summary>
    internal DbSet<StockEventRow> StockEvents => Set<StockEventRow>();

    /// <inheritdoc />
    public DbSet<CurrentStockLevelRow> CurrentStockLevels => Set<CurrentStockLevelRow>();

    /// <inheritdoc />
    public DbSet<ReservationAuditRow> ReservationAudit => Set<ReservationAuditRow>();

    /// <inheritdoc />
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Harmless today (no SmartEnums persisted) but retained so future
        // Status/ReleaseReason migrations to SmartEnum land without ceremony.
        configurationBuilder.ConfigureSmartEnum();
    }
}
