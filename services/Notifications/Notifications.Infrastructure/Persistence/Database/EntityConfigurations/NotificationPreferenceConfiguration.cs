using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;

namespace Notifications.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF Core mapping for the <see cref="NotificationPreference"/> reference table — seeded recipient
/// preference + contact data keyed by <c>user_id</c> (the Keycloak sub). One row per user so channel
/// resolution needs no join (notifications.md § 8).
/// </summary>
/// <remarks>
/// Two mappings are non-default here:
/// <list type="bullet">
/// <item><b><c>enabled_channels</c> → PG <c>text[]</c>.</b> A collection of <see cref="ChannelType"/>
/// (SmartEnum) needs both a <see cref="ValueConverter"/> (list ↔ string array of names) <i>and</i> an
/// explicit <see cref="ValueComparer"/>: without a comparer EF treats the mutable reference type by
/// reference identity and misses content changes / corrupts the change-tracker snapshot. (No in-repo
/// precedent — existing SmartEnum conversions are all single-value.)</item>
/// <item><b><c>quiet_hours_start/end</c> → PG <c>time</c>.</b> Mapped from <see cref="System.TimeOnly"/>
/// (Npgsql native) — civil wall-clock, the one documented exception to ADR-0015's <c>DateTimeOffset</c>
/// rule (a recurring time-of-day window is not an instant). Nullable = no quiet hours.</item>
/// </list>
/// </remarks>
internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("user_preferences", t => t.HasComment(
            "Seeded recipient preference + contact reference, keyed user_id (Keycloak sub). notifications.md §8."));

        builder.HasKey(p => p.UserId);

        builder.Property(p => p.UserId)
            .HasComment("Recipient identity — the Keycloak sub; equals the command's RecipientUserId.");

        builder.Property(p => p.Email)
            .HasComment("Email address the email dispatcher delivers to.");

        builder.Property(p => p.PhoneNumber)
            .HasComment("Fake E.164 phone number (SMS is a fake channel); consumed by the SMS dispatcher (#315).");

        var channelsConverter = new ValueConverter<IReadOnlyList<ChannelType>, string[]>(
            channels => channels.Select(channel => channel.Name).ToArray(),
            names => names.Select(name => ChannelType.FromName(name)).ToList());

        // SmartEnum names are stable, so hashing/equality over the names is a safe structural comparer.
        var channelsComparer = new ValueComparer<IReadOnlyList<ChannelType>>(
            (left, right) => left!.SequenceEqual(right!),
            channels => channels.Aggregate(0, (hash, channel) => HashCode.Combine(hash, channel.Value)),
            channels => channels.ToList());

        builder.Property(p => p.EnabledChannels)
            .HasColumnType("text[]")
            .HasConversion(channelsConverter, channelsComparer)
            .HasComment("Channels the recipient enabled — the left operand of enabled ∩ template_channels (§5.3).");

        builder.Property(p => p.QuietHoursStart)
            .HasColumnType("time")
            .HasComment("Start of the daily quiet-hours window (civil wall-clock in time_zone); null = no quiet hours.");

        builder.Property(p => p.QuietHoursEnd)
            .HasColumnType("time")
            .HasComment("End of the quiet-hours window; null with quiet_hours_start (both-or-neither).");

        builder.Property(p => p.TimeZone)
            .HasComment("IANA time zone (e.g. Europe/Prague) the quiet-hours window is interpreted in.");
    }
}
