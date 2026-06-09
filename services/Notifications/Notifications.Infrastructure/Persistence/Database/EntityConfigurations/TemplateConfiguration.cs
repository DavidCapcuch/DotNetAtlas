using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Templates;

namespace Notifications.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF Core mapping for the <see cref="Template"/> reference table — seeded notification templates
/// keyed by <c>template_key</c>. Per-channel renderable content lives in the child
/// <c>template_channels</c> table (<see cref="TemplateChannelConfiguration"/>). See ADR-0032 § 7.
/// </summary>
internal sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    internal const int TemplateKeyMaxLength = 128;
    private const int DescriptionMaxLength = 256;

    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates", t => t.HasComment(
            "Seeded notification template reference data, keyed {bc}.{type} (lower-kebab). ADR-0032 §7."));

        builder.HasKey(t => t.TemplateKey);

        builder.Property(t => t.TemplateKey)
            .HasMaxLength(TemplateKeyMaxLength)
            .HasComment("Template identity {bounded-context}.{notification-type} (lower-kebab).");

        builder.Property(t => t.Description)
            .HasMaxLength(DescriptionMaxLength)
            .HasComment("Human-readable description of what this template notifies about.");
    }
}
