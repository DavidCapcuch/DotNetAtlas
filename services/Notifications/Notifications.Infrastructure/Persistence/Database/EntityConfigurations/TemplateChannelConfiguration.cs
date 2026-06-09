using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Channels;
using Notifications.Domain.Templates;

namespace Notifications.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF Core mapping for <see cref="TemplateChannel"/> — the per-channel renderable content keyed
/// (<c>template_key</c>, <c>channel_type</c>), with a foreign key to the parent <c>templates</c> row.
/// The rows for a template key are also its "supported channels" set (channel resolution, #314).
/// <see cref="ChannelType"/> persists as its readable name; <c>subject</c> is nullable, <c>body</c>
/// required. See ADR-0032 § 7.
/// </summary>
internal sealed class TemplateChannelConfiguration : IEntityTypeConfiguration<TemplateChannel>
{
    private const int ChannelMaxLength = 16;
    private const int SubjectMaxLength = 256;

    public void Configure(EntityTypeBuilder<TemplateChannel> builder)
    {
        builder.ToTable("template_channels", t => t.HasComment(
            "Per-channel template content + the supported-channel set, keyed (template_key, channel_type). ADR-0032 §7."));

        builder.HasKey(tc => new { tc.TemplateKey, tc.Channel });

        builder.Property(tc => tc.TemplateKey)
            .HasMaxLength(TemplateConfiguration.TemplateKeyMaxLength)
            .HasComment("Owning template's key (FK to templates.template_key).");

        builder.Property(tc => tc.Channel)
            .HasMaxLength(ChannelMaxLength)
            .HasConversion(
                channel => channel.Name,
                name => ChannelType.FromName(name))
            .HasComment("Delivery channel (Email|Sms|Bell) this content renders for.");

        builder.Property(tc => tc.Subject)
            .HasMaxLength(SubjectMaxLength)
            .HasComment("Subject-line template with {{token}} placeholders; null for channels without a subject.");

        // Intentionally unbounded (text) — bodies are full message content (multi-line email),
        // unlike the length-capped key/subject/channel columns.
        builder.Property(tc => tc.Body)
            .HasComment("Body template with {{token}} placeholders.");

        builder.HasOne<Template>()
            .WithMany()
            .HasForeignKey(tc => tc.TemplateKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
