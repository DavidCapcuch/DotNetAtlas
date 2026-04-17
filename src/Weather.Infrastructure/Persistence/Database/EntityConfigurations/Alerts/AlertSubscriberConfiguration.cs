using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Infrastructure.Persistence.Database.EntityConfigurations.Alerts;

public class AlertSubscriberConfiguration : IEntityTypeConfiguration<AlertSubscriber>
{
    public void Configure(EntityTypeBuilder<AlertSubscriber> builder)
    {
        builder.ToTable(t => t.HasComment("Contains subscribers for weather alert subscriptions."));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasComment("PK")
            .ValueGeneratedNever();

        builder.Property(s => s.UserId)
            .HasComment("User who subscribed for weather alerts.");

        builder.HasIndex(s => s.UserId)
            .IsUnique()
            .HasDatabaseName("UX_Subscribers_UserId");

        builder.Property(s => s.SubscriptionTier)
            .HasComment("Subscription tier (Free, Pro, Ultra).")
            .HasConversion(
                tier => tier.Value,
                value => SubscriptionTier.FromValue(value));

        builder.Property(s => s.TemperatureUnitPreference)
            .HasComment("Preferred temperature unit (Celsius, Fahrenheit, Kelvin).")
            .HasConversion(
                unit => unit.Value,
                value => TemperatureUnit.FromValue(value));

        builder.Property(s => s.WindSpeedUnitPreference)
            .HasComment("Preferred wind speed unit (KilometersPerHour, MilesPerHour).")
            .HasConversion(
                unit => unit.Value,
                value => WindSpeedUnit.FromValue(value));

        builder.Property(s => s.CreatedUtc)
            .HasComment("Timestamp when user first subscribed (UTC).");

        builder.Property(s => s.LastModifiedUtc)
            .HasComment("Timestamp when subscription was last modified (UTC).");

        builder.Property(s => s.SubscriptionExpiryAtUtc)
            .HasComment("Expiry date for subscription (UTC). Null for free tier.");

        builder.Property(s => s.LastPaidSubscriptionEndedAtUtc)
            .HasComment("When the last paid subscription ended. Null if never had paid subscription.");

        builder.HasIndex(s => new
        {
            s.SubscriptionTier,
            s.SubscriptionExpiryAtUtc
        })
            .HasDatabaseName("IX_Subscribers_SubscriptionTier_ExpiryUtc");

        builder.HasMany(s => s.MonitoredLocationAlertsSubscriptions)
            .WithOne()
            .HasForeignKey("AlertSubscriberId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(s => s.RowVersion)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");
    }
}
