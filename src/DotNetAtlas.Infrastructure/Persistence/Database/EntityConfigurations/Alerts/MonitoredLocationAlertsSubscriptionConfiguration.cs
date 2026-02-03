using DotNetAtlas.Domain.Alerts.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetAtlas.Infrastructure.Persistence.Database.EntityConfigurations.Alerts;

public class
    MonitoredLocationAlertsSubscriptionConfiguration : IEntityTypeConfiguration<MonitoredLocationAlertsSubscription>
{
    public void Configure(EntityTypeBuilder<MonitoredLocationAlertsSubscription> builder)
    {
        builder.ToTable(t => t.HasComment("Contains user subscriptions to monitored location weather alerts."));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasComment("PK")
            .ValueGeneratedNever();

        builder.Property(s => s.MonitoredLocationId)
            .HasComment("FK to MonitoredLocation (ID reference only, no navigation).");

        builder.HasIndex(s => s.MonitoredLocationId)
            .HasDatabaseName("IX_MonitoredLocationAlertsSubscriptions_MonitoredLocationId");

        builder.Property(s => s.CreatedUtc)
            .HasComment("Creation timestamp (UTC).");

        builder.Property(s => s.LastModifiedUtc)
            .HasComment("Last modification timestamp (UTC).");

        builder.Property(s => s.Timestamp)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");
    }
}
