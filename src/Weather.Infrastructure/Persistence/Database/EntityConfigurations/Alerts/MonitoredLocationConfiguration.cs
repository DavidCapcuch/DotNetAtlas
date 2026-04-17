using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weather.Domain.Alerts;

namespace Weather.Infrastructure.Persistence.Database.EntityConfigurations.Alerts;

public class MonitoredLocationConfiguration : IEntityTypeConfiguration<MonitoredLocation>
{
    public void Configure(EntityTypeBuilder<MonitoredLocation> builder)
    {
        builder.ToTable(t =>
            t.HasComment("Contains monitored locations with weather sensor data and alert thresholds."));

        builder.HasKey(ml => ml.Id);
        builder.Property(ml => ml.Id)
            .HasComment("PK")
            .ValueGeneratedNever();

        builder.HasOne(ml => ml.Location)
            .WithOne()
            .HasForeignKey<MonitoredLocation>("LocationId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(ml => ml.IsActive)
            .HasComment("Whether this location is actively being monitored.");

        // AlertThresholds as complex property with nested value objects
        // Note: Using internal properties (Celsius/KilometersPerHour/Percent) for DB persistence
        builder.ComplexProperty(ml => ml.Thresholds, thresholdsBuilder =>
        {
            thresholdsBuilder.ComplexProperty(t => t.HighTemperature, tempBuilder =>
            {
                tempBuilder.Property("Celsius")
                    .HasColumnName("HighTemperatureThresholdC")
                    .HasComment("Temperature threshold for high temperature alerts (°C).");
            });

            thresholdsBuilder.ComplexProperty(t => t.LowTemperature, tempBuilder =>
            {
                tempBuilder.Property("Celsius")
                    .HasColumnName("LowTemperatureThresholdC")
                    .HasComment("Temperature threshold for low temperature alerts (°C).");
            });

            thresholdsBuilder.ComplexProperty(t => t.HighWindSpeed, windBuilder =>
            {
                windBuilder.Property("KilometersPerHour")
                    .HasColumnName("HighWindSpeedThresholdKmh")
                    .HasComment("Wind speed threshold for high wind alerts (km/h).");
            });

            thresholdsBuilder.ComplexProperty(t => t.HighHumidity, humidityBuilder =>
            {
                humidityBuilder.Property("Percent")
                    .HasColumnName("HighHumidityThresholdPercent")
                    .HasComment("Humidity threshold for high humidity alerts (%).");
            });

            thresholdsBuilder.ComplexProperty(t => t.LowHumidity, humidityBuilder =>
            {
                humidityBuilder.Property("Percent")
                    .HasColumnName("LowHumidityThresholdPercent")
                    .HasComment("Humidity threshold for low humidity alerts (%).");
            });
        });

        // Weather readings stored as JSON collection with nested value objects
        // Note: Using internal properties (Celsius/KilometersPerHour/Percent) for DB persistence
        builder.OwnsMany(ml => ml.RecentReadings, readingsBuilder =>
        {
            readingsBuilder.ToJson("RecentReadings");

            // Temperature value object - store Celsius value
            readingsBuilder.OwnsOne(r => r.Temperature, tempBuilder =>
            {
                tempBuilder.Property("Celsius").HasJsonPropertyName("temperatureC");
            });

            // Humidity value object - store percent value
            readingsBuilder.OwnsOne(r => r.Humidity, humidityBuilder =>
            {
                humidityBuilder.Property("Percent").HasJsonPropertyName("humidityPercent");
            });

            // WindSpeed value object - store km/h value
            readingsBuilder.OwnsOne(r => r.WindSpeed, windBuilder =>
            {
                windBuilder.Property("KilometersPerHour").HasJsonPropertyName("windSpeedKmh");
            });

            readingsBuilder.Property(r => r.RecordedAtUtc).HasJsonPropertyName("recordedAtUtc");
        });

        builder.Property(ml => ml.CreatedUtc)
            .HasComment("Creation timestamp (UTC).");

        builder.Property(ml => ml.LastModifiedUtc)
            .HasComment("Last modification timestamp (UTC).");

        builder.Property(s => s.RowVersion)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");
    }
}
