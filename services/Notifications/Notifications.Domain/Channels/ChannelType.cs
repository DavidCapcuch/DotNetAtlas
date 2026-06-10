using Ardalis.SmartEnum;

namespace Notifications.Domain.Channels;

/// <summary>
/// A delivery channel Notifications fans a notification out to. v2 defines three channels with two
/// behavioural flags: <see cref="RespectsQuietHours"/> (SMS defers inside the recipient's
/// quiet-hours window) and <see cref="IsDurable"/> (the bell is ephemeral — no ledger, no delivery
/// event, minimal retries). All three dispatchers are wired: email (#312), fake SMS (#315) and the
/// SignalR bell (#317). See ADR-0032.
/// </summary>
public sealed class ChannelType : SmartEnum<ChannelType>
{
    public static readonly ChannelType Email = new(nameof(Email), 1, respectsQuietHours: false, isDurable: true);
    public static readonly ChannelType Sms = new(nameof(Sms), 2, respectsQuietHours: true, isDurable: true);
    public static readonly ChannelType Bell = new(nameof(Bell), 3, respectsQuietHours: false, isDurable: false);

    private ChannelType(string name, int value, bool respectsQuietHours, bool isDurable)
        : base(name, value)
    {
        RespectsQuietHours = respectsQuietHours;
        IsDurable = isDurable;
    }

    /// <summary>
    /// True when delivery on this channel must be deferred out of the recipient's quiet-hours
    /// window (SMS today; a future push channel inherits the deferral for free). The Kafka handler
    /// computes the deferral per channel via <c>QuietHoursCalculator</c> (notifications.md § 5.4).
    /// </summary>
    public bool RespectsQuietHours { get; }

    /// <summary>
    /// True when the channel records a <c>(NotificationId, Channel)</c> ledger row and emits a
    /// delivery event (email, SMS). Durable channels dispatch via the full-retry
    /// <c>NotificationDispatchJob</c>; ephemeral ones (bell) via the minimal-retry
    /// <c>EphemeralNotificationDispatchJob</c> — the ledger/event writes themselves live inside each
    /// channel's dispatcher, not behind this flag (ADR-0032 § 2/§ 3).
    /// </summary>
    public bool IsDurable { get; }
}
