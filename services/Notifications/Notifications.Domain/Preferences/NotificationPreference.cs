using Notifications.Domain.Channels;

namespace Notifications.Domain.Preferences;

/// <summary>
/// A recipient's notification preference + contact details — seeded local reference data (notifications.md
/// § 8), <b>not</b> a projection: there is no Identity/Accounts BC (ADR-0005), so <see cref="UserId"/> is the
/// Keycloak <c>sub</c> and Notifications owns the slice of user data it needs. One row per user; the handler
/// resolves <c>enabled_channels ∩ template_channels</c> (<see cref="ChannelResolver"/>) and the email
/// dispatcher resolves the address from <see cref="Email"/>. Not an aggregate root — there is no runtime
/// mutation surface (no preference HTTP; deferred seam, § 13), so no invariant-guarded object graph.
/// </summary>
public sealed class NotificationPreference
{
    private NotificationPreference(
        Guid userId,
        string email,
        string phoneNumber,
        IReadOnlyList<ChannelType> enabledChannels,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        string timeZone)
    {
        UserId = userId;
        Email = email;
        PhoneNumber = phoneNumber;
        EnabledChannels = enabledChannels;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        TimeZone = timeZone;
    }

    // EF Core materialisation constructor.
    private NotificationPreference()
    {
    }

    /// <summary>Primary key; equals the command's <c>RecipientUserId</c> (see the class summary for the sub-as-identity rationale).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Email address the email dispatcher delivers to.</summary>
    public string Email { get; private set; } = null!;

    /// <summary>Fake E.164 phone number (SMS is a fake channel in v2). Consumed by the SMS dispatcher (#315).</summary>
    public string PhoneNumber { get; private set; } = null!;

    /// <summary>The channels this recipient has enabled — the left operand of the resolution rule (§ 5.3).</summary>
    public IReadOnlyList<ChannelType> EnabledChannels { get; private set; } = [];

    /// <summary>
    /// Start of the recipient's daily quiet-hours window (civil wall-clock in <see cref="TimeZone"/>); <c>null</c>
    /// when the recipient has no quiet hours. Consumed by the quiet-hours scheduler (#315), not v2 dispatch.
    /// </summary>
    public TimeOnly? QuietHoursStart { get; private set; }

    /// <summary>End of the quiet-hours window; <c>null</c> with <see cref="QuietHoursStart"/> (both-or-neither).</summary>
    public TimeOnly? QuietHoursEnd { get; private set; }

    /// <summary>IANA time zone (e.g. <c>Europe/Prague</c>) the quiet-hours window is interpreted in (ADR-0015 § quiet hours).</summary>
    public string TimeZone { get; private set; } = null!;

    /// <summary>Creates a preference reference row (used by the dev seeder and tests).</summary>
    public static NotificationPreference Create(
        Guid userId,
        string email,
        string phoneNumber,
        IReadOnlyList<ChannelType> enabledChannels,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        string timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentNullException.ThrowIfNull(enabledChannels);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);

        // Quiet hours are an all-or-nothing window; a half-specified bound is meaningless to the
        // quiet-hours scheduler (#315) and signals a seeding/caller bug.
        if (quietHoursStart.HasValue != quietHoursEnd.HasValue)
        {
            throw new ArgumentException(
                "Quiet hours must specify both a start and an end, or neither.", nameof(quietHoursStart));
        }

        return new NotificationPreference(
            userId, email, phoneNumber, enabledChannels.ToArray(), quietHoursStart, quietHoursEnd, timeZone);
    }
}
