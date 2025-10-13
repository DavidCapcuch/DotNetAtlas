using DotNetAtlas.Domain.Entities.Weather.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetAtlas.Infrastructure.Persistence.Database.ContextConfigurations;

public class WeatherFeedbackConfiguration : IEntityTypeConfiguration<WeatherFeedback>
{
    public void Configure(EntityTypeBuilder<WeatherFeedback> builder)
    {
        builder.HasKey(wf => wf.Id);
        builder.Property(wf => wf.Id)
            .HasComment("PK")
            .ValueGeneratedNever();

        builder.ToTable(wf => wf.HasComment("Contains user feedbacks about the weather."));

        builder.Property(wf => wf.CreatedByUser)
            .HasComment("User who created the feedback.")
            .IsRequired();

        builder.HasIndex(wf => wf.CreatedByUser)
            .IsUnique()
            .HasDatabaseName("UX_WeatherFeedback_CreatedByUser");

        builder.Property<byte[]>("Timestamp")
            .HasComment("Optimistic concurrency token.")
            .IsRowVersion();

        builder.ComplexProperty(wf => wf.Rating, r =>
        {
            r.Property(x => x.Value)
                .HasColumnName("Rating")
                .HasComment("Rating given by the user.");
        });

        builder.ComplexProperty(wf => wf.Feedback, f =>
        {
            f.Property(x => x.Value)
                .HasColumnName("Feedback")
                .HasMaxLength(500)
                .HasComment("Weather feedback from the user.");
        });

        builder.Property(wf => wf.CreatedUtc)
            .HasComment("Creation timestamp (UTC).");

        builder.Property(wf => wf.LastModifiedUtc)
            .HasComment("Last modification timestamp (UTC).");
    }
}
