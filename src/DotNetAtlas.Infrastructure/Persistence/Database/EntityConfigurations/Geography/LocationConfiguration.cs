using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Common.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetAtlas.Infrastructure.Persistence.Database.EntityConfigurations.Geography;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable(t => t.HasComment("Contains city-country locations."));

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasComment("PK")
            .ValueGeneratedNever();

        builder.ComplexProperty(l => l.City, cityBuilder =>
        {
            cityBuilder.Property(c => c.Name)
                .HasColumnName("City")
                .HasMaxLength(City.MaxLength)
                .HasComment("Name of the city.");
        });

        builder.Property(l => l.CountryCode)
            .HasComment("ISO 3166-1 alpha-2 country code.");

        builder.Property(l => l.CreatedUtc)
            .HasComment("Creation timestamp (UTC).");

        builder.Property(l => l.LastModifiedUtc)
            .HasComment("Last modification timestamp (UTC).");

        builder.Property(s => s.Timestamp)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");
    }
}
