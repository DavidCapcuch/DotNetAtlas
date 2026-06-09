using Ardalis.SmartEnum;

namespace Notifications.Domain.Channels;

/// <summary>
/// A delivery channel Notifications fans a notification out to. v2 defines three channels;
/// the only behavioural difference is <see cref="RespectsQuietHours"/> (SMS defers inside the
/// recipient's quiet-hours window). Email (#312) and the fake SMS (#315) dispatchers are wired;
/// the bell lands in a later slice. See ADR-0032.
/// </summary>
public sealed class ChannelType : SmartEnum<ChannelType>
{
    public static readonly ChannelType Email = new(nameof(Email), 1, respectsQuietHours: false);
    public static readonly ChannelType Sms = new(nameof(Sms), 2, respectsQuietHours: true);
    public static readonly ChannelType Bell = new(nameof(Bell), 3, respectsQuietHours: false);

    private ChannelType(string name, int value, bool respectsQuietHours)
        : base(name, value)
    {
        RespectsQuietHours = respectsQuietHours;
    }

    /// <summary>
    /// True when delivery on this channel must be deferred out of the recipient's quiet-hours
    /// window (SMS today; a future push channel inherits the deferral for free). The Kafka handler
    /// computes the deferral per channel via <c>QuietHoursCalculator</c> (notifications.md § 5.4).
    /// </summary>
    public bool RespectsQuietHours { get; }
}
