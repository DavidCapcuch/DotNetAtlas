using Inventory.Infrastructure.Persistence.EventStore;
using Microsoft.EntityFrameworkCore;
using SmartEnum.EFCore;

namespace Inventory.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Inventory bounded context. M3 scope: the ES
/// write model only — a single <c>DbSet&lt;StockEventRow&gt;</c> against the
/// <c>inventory.stock_events</c> table with PK <c>(StreamId, Version)</c>.
/// Outbox/inbox wiring and projection tables land in M4+.
/// </summary>
public sealed class InventoryDbContext : DbContext
{
    /// <summary>Default Postgres schema for all Inventory tables.</summary>
    public const string DefaultSchemaName = "inventory";

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    /// <summary>Append-only ES stream — <c>inventory.stock_events</c>.</summary>
    internal DbSet<StockEventRow> StockEvents => Set<StockEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        // Outbox/inbox configurations are added in M4 once the application
        // layer has publishers and saga-command consumers to wire up.
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Harmless in M3 (no SmartEnums persisted yet); lands now to keep
        // parity with Ordering/Weather so M4's projections pick it up
        // automatically.
        configurationBuilder.ConfigureSmartEnum();
    }
}
