using Inventory.Infrastructure.Persistence.EventStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="StockEventRow"/>. Enforces the append-only ES
/// write model defined in <c>docs/bc-design/inventory.md § 8.1</c>:
/// composite PK <c>(StreamId, Version)</c> for optimistic concurrency,
/// <c>jsonb</c> payload, DB-side <c>AppendedAtUtc</c>, and the three
/// secondary indexes for temporal, correlation, and event-type queries.
/// </summary>
internal sealed class StockEventRowConfiguration : IEntityTypeConfiguration<StockEventRow>
{
    public void Configure(EntityTypeBuilder<StockEventRow> builder)
    {
        builder.ToTable("stock_events", t => t.HasComment(
            "Append-only event store for StockItem aggregates (ADR-0006). "
            + "One row per internal ES event; composite PK (StreamId, Version) "
            + "is the optimistic-concurrency mechanism."));

        builder.HasKey(r => new { r.StreamId, r.Version });

        builder.Property(r => r.StreamId)
            .HasComment("Stream identity = ProductId. One stream per StockItem.");

        builder.Property(r => r.Version)
            .HasComment("Monotonic 1-based version per stream. Enforced by PK.");

        builder.Property(r => r.EventType)
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("CLR-type name discriminator (e.g. \"StockReservedDomainEvent\") used by the deserializer.");

        builder.Property(r => r.Payload)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasComment("JSON-serialized internal event; stored as jsonb for legibility and indexability.");

        builder.Property(r => r.OccurredAtUtc)
            .HasComment("UTC timestamp the domain event was produced; copied from event.OccurredOnUtc for temporal queries.");

        builder.Property(r => r.AppendedAtUtc)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd()
            .HasComment("DB-side insert timestamp; distinguishes domain time from persisted time during replay/tests.");

        builder.Property(r => r.CorrelationId)
            .HasComment("Saga correlation id (ADR-0008); null for ops-originated events.");

        // Secondary indexes per inventory.md § 8.1.
        builder.HasIndex(r => r.OccurredAtUtc)
            .HasDatabaseName("ix_stock_events_occurred_at");

        builder.HasIndex(r => r.EventType)
            .HasDatabaseName("ix_stock_events_event_type");

        builder.HasIndex(r => r.CorrelationId)
            .HasDatabaseName("ix_stock_events_correlation")
            .HasFilter("correlation_id IS NOT NULL");
    }
}
