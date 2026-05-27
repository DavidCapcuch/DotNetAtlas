using Inventory.Application.Common.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="CurrentStockLevelRow"/> — the hot-path stock
/// projection described in <c>docs/bc-design/inventory.md</c> § 9.1. Table is
/// upserted by <c>CurrentStockLevelsProjectionDomainEventHandler</c> inside the same
/// transaction as every <c>stock_events</c> append.
/// </summary>
internal sealed class CurrentStockLevelRowConfiguration : IEntityTypeConfiguration<CurrentStockLevelRow>
{
    public void Configure(EntityTypeBuilder<CurrentStockLevelRow> builder)
    {
        builder.ToTable("current_stock_levels", t => t.HasComment(
            "Denormalised read projection: one row per ProductId, mutated by "
            + "CurrentStockLevelsProjectionDomainEventHandler on every ES event. "
            + "Rebuildable from inventory.stock_events."));

        builder.HasKey(r => r.ProductId);

        builder.Property(r => r.ProductId)
            .HasComment("Aggregate identity / stream id. Shared with Catalog's Product.ProductId.");

        builder.Property(r => r.OnHand)
            .HasComment("Physical units present after the last applied event.");

        builder.Property(r => r.Reserved)
            .HasComment("Active reservations total after the last applied event.");

        builder.Property(r => r.Available)
            .HasComment("OnHand - Reserved after the last applied event. Materialised for indexable reads.");

        builder.Property(r => r.PreviousAvailable)
            .HasComment("Available BEFORE the last applied event; enables StockLevelChanged threshold detection without state replay.");

        builder.Property(r => r.LastUpdatedUtc)
            .HasComment("= OccurredOnUtc of the last applied event.");

        builder.Property(r => r.LastVersion)
            .HasComment("Monotonic per-stream event count applied to this row — guards future projection rebuilds against duplicates.");

        // Partial index for low-stock dashboards + procurement alerts
        // (inventory.md § 9.1). Filter keeps the index tiny for the common
        // "everything's in stock" case.
        builder.HasIndex(r => r.Available)
            .HasDatabaseName("ix_current_stock_levels_available_low")
            .HasFilter("available <= 10");
    }
}
