using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.Infrastructure.Persistence.Database.EntityConfigurations.Feedback;

public class FeedbackConfiguration : IEntityTypeConfiguration<Weather.Domain.Feedback.Feedback>
{
    public void Configure(EntityTypeBuilder<Weather.Domain.Feedback.Feedback> builder)
    {
        builder.HasKey(wf => wf.Id);
        builder.Property(wf => wf.Id)
            .HasComment("PK")
            .ValueGeneratedNever();

        builder.ToTable(wf => wf.HasComment("Contains user feedbacks about the weather."));

        builder.Property(wf => wf.CreatedByUser)
            .HasComment("User who created the feedback.");

        builder.HasIndex(wf => wf.CreatedByUser)
            .IsUnique()
            .HasDatabaseName("UX_WeatherFeedback_CreatedByUser");

        builder.Property(s => s.Timestamp)
            .IsRowVersion()
            .HasComment("Optimistic concurrency token.");

        builder.ComplexProperty(wf => wf.Rating, r =>
        {
            r.Property(x => x.Value)
                .HasColumnName("Rating")
                .HasComment("Rating given by the user.");
        });

        builder.ComplexProperty(wf => wf.FeedbackText, f =>
        {
            f.Property(x => x.Text)
                .HasColumnName("Feedback")
                .HasMaxLength(FeedbackText.TextMaxLength)
                .HasComment("Weather feedback from the user.");
        });

        builder.Property(wf => wf.CreatedUtc)
            .HasComment("Creation timestamp (UTC).");

        builder.Property(wf => wf.LastModifiedUtc)
            .HasComment("Last modification timestamp (UTC).");
    }
}
