using Inventory.Application.Common.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF mapping for <see cref="ReservationAuditRow"/> — the reservation-lifecycle
/// projection described in <c>docs/bc-design/inventory.md</c> § 9.2. One row
/// per reservation; fan-in indexes on <c>OrderId</c> and the partial
/// active-expiry index feed the <c>ReservationExpiryWorker</c>'s scan.
/// </summary>
internal sealed class ReservationAuditRowConfiguration : IEntityTypeConfiguration<ReservationAuditRow>
{
    public void Configure(EntityTypeBuilder<ReservationAuditRow> builder)
    {
        builder.ToTable("reservation_audit", t => t.HasComment(
            "Per-reservation lifecycle projection. Inserted on StockReservedDomainEvent, "
            + "terminal fields (Status, ResolvedAtUtc, ReleaseReason) mutated on "
            + "Confirmed / Released. Ops + expiry-worker query surface."));

        builder.HasKey(r => r.ReservationId);

        builder.Property(r => r.ReservationId)
            .HasComment("Saga-supplied reservation id (GUIDv7).");

        builder.Property(r => r.ProductId)
            .HasComment("Stream id joining back to inventory.current_stock_levels.ProductId.");

        builder.Property(r => r.OrderId)
            .HasComment("Owning order. Fan-in key for saga correlation.");

        builder.Property(r => r.Quantity)
            .HasComment("Units reserved. Immutable after the initial insert.");

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasComment("Lifecycle: Active -> Confirmed | Released.");

        builder.Property(r => r.ReservedAtUtc)
            .HasComment("UTC timestamp the reservation was created.");

        builder.Property(r => r.ExpiresAtUtc)
            .HasComment("UTC expiry (= ReservedAtUtc + TTL). Drives the TTL worker scan.");

        builder.Property(r => r.ResolvedAtUtc)
            .HasComment("UTC timestamp of the terminal transition; null while Active.");

        builder.Property(r => r.ReleaseReason)
            .HasConversion<string?>()
            .HasMaxLength(16)
            .HasComment("Populated only on Status=Released (Compensation | Expiry | Cancellation).");

        // Fan-in query: "all reservations for order X". Saga-side debugging +
        // compensation support.
        builder.HasIndex(r => r.OrderId)
            .HasDatabaseName("ix_reservation_audit_order");

        // Drives the ReservationExpiryWorker scan: WHERE Status='Active' AND
        // ExpiresAtUtc < now(). Partial index keeps it tiny — rows flip out of
        // the index as they reach a terminal state.
        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasDatabaseName("ix_reservation_audit_active_expiry")
            .HasFilter("status = 'Active'");
    }
}
