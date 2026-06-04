using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;

namespace Notifications.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF Core mapping for the <see cref="NotificationDelivery"/> per-channel ledger. The composite
/// primary key <c>(notification_id, channel)</c> is the unique idempotency guard (ADR-0031/0032):
/// the dispatcher INSERTs the first attempt and UPDATEs in place on retry, so a second INSERT for the
/// same key would hit this PK and throw. <see cref="ChannelType"/> and <see cref="DeliveryStatus"/>
/// persist as their readable string forms.
/// </summary>
internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    private const int ChannelMaxLength = 16;
    private const int StatusMaxLength = 16;

    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries", t => t.HasComment(
            "Per-channel delivery ledger — idempotency + audit, keyed (notification_id, channel). ADR-0031/0032."));

        builder.HasKey(d => new { d.NotificationId, d.Channel });

        builder.Property(d => d.NotificationId)
            .HasComment("Producer-assigned notification intent identity (half of the ledger key).");

        builder.Property(d => d.Channel)
            .HasMaxLength(ChannelMaxLength)
            .HasConversion(
                channel => channel.Name,
                name => ChannelType.FromName(name))
            .HasComment("Delivery channel (Email|Sms|Bell) — the other half of the ledger key.");

        builder.Property(d => d.Status)
            .HasMaxLength(StatusMaxLength)
            .HasConversion<string>()
            .HasComment("Latest recorded outcome (Dispatched|Failed).");

        builder.Property(d => d.CreatedAtUtc)
            .HasComment("UTC timestamp when the row was first inserted.");

        builder.Property(d => d.UpdatedAtUtc)
            .HasComment("UTC timestamp of the latest status write.");
    }
}
