using AwesomeAssertions;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Xunit;

namespace Notifications.UnitTests.Preferences;

/// <summary>
/// <see cref="NotificationPreference"/> is seeded reference data (notifications.md § 8) with no runtime
/// mutation surface; the only behaviour to guard is construction — the quiet-hours window shape
/// (both-or-neither, non-equal bounds, ≤ 23h) and time-zone resolvability that
/// <see cref="QuietHoursCalculator"/> relies on (#315), plus contact-field basics.
/// </summary>
public sealed class NotificationPreferenceTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void Create_WithoutQuietHours_Succeeds()
    {
        // Act
        var preference = NotificationPreference.Create(
            UserId,
            email: "d.capcuch@gmail.com",
            phoneNumber: "+420600000003",
            enabledChannels: [ChannelType.Email, ChannelType.Sms, ChannelType.Bell],
            quietHoursStart: null,
            quietHoursEnd: null,
            timeZone: "Europe/Prague");

        // Assert
        using (new AssertionScope())
        {
            preference.UserId.Should().Be(UserId);
            preference.Email.Should().Be("d.capcuch@gmail.com");
            preference.EnabledChannels.Should().Equal(ChannelType.Email, ChannelType.Sms, ChannelType.Bell);
            preference.QuietHoursStart.Should().BeNull();
            preference.QuietHoursEnd.Should().BeNull();
        }
    }

    [Fact]
    public void Create_WithBothQuietHourBounds_Succeeds()
    {
        // Act
        var preference = NotificationPreference.Create(
            UserId,
            email: "d.capcuch@gmail.com",
            phoneNumber: "+420600000003",
            enabledChannels: [ChannelType.Email],
            quietHoursStart: new TimeOnly(22, 0),
            quietHoursEnd: new TimeOnly(7, 0),
            timeZone: "Europe/Prague");

        // Assert
        using (new AssertionScope())
        {
            preference.QuietHoursStart.Should().Be(new TimeOnly(22, 0));
            preference.QuietHoursEnd.Should().Be(new TimeOnly(7, 0));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Create_WithOnlyOneQuietHourBound_Throws(bool hasStart, bool hasEnd)
    {
        // Arrange
        var start = hasStart ? new TimeOnly(22, 0) : (TimeOnly?)null;
        var end = hasEnd ? new TimeOnly(7, 0) : (TimeOnly?)null;

        // Act
        var act = () => NotificationPreference.Create(
            UserId, "d.capcuch@gmail.com", "+420600000003", [ChannelType.Email], start, end, "Europe/Prague");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEqualQuietHourBounds_Throws()
    {
        // Act — start == end is an empty [start, end) window: silently never-quiet, which is
        // almost certainly not what the configurer meant ("always quiet" = disable the channel).
        var act = () => NotificationPreference.Create(
            UserId, "d.capcuch@gmail.com", "+420600000003", [ChannelType.Email],
            new TimeOnly(22, 0), new TimeOnly(22, 0), "Europe/Prague");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 1, 23, 59)] // 23h58m, same-day window
    [InlineData(1, 0, 0, 30)] // 23h30m, wrapping past midnight
    public void Create_WithQuietWindowLongerThan23Hours_Throws(
        int startHour, int startMinute, int endHour, int endMinute)
    {
        // Act — near-24h windows could make consecutive daily windows overlap across a DST shift,
        // which QuietHoursCalculator's anchor probe does not defend against.
        var act = () => NotificationPreference.Create(
            UserId, "d.capcuch@gmail.com", "+420600000003", [ChannelType.Email],
            new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute), "Europe/Prague");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithQuietWindowOfExactly23Hours_Succeeds()
    {
        // Act — 23h is the inclusive cap; only longer windows are rejected.
        var preference = NotificationPreference.Create(
            UserId, "d.capcuch@gmail.com", "+420600000003", [ChannelType.Email],
            new TimeOnly(0, 0), new TimeOnly(23, 0), "Europe/Prague");

        // Assert
        preference.QuietHoursEnd.Should().Be(new TimeOnly(23, 0));
    }

    [Fact]
    public void Create_WithUnresolvableTimeZone_ThrowsNamingTheValue()
    {
        // Act — a typo'd IANA id must fail here, at the construction boundary, not hours later at
        // quiet-hours fan-out where it would DLT the whole command (all channels).
        var act = () => NotificationPreference.Create(
            UserId, "d.capcuch@gmail.com", "+420600000003", [ChannelType.Email],
            new TimeOnly(22, 0), new TimeOnly(7, 0), "Europe/Pragu");

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Europe/Pragu*");
    }

    [Fact]
    public void Create_WithBlankEmail_Throws()
    {
        // Act
        var act = () => NotificationPreference.Create(
            UserId, "  ", "+420600000003", [ChannelType.Email], null, null, "Europe/Prague");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TakesADefensiveCopyOfEnabledChannels()
    {
        // Arrange
        var channels = new List<ChannelType> { ChannelType.Email };

        var preference = NotificationPreference.Create(
            UserId, "d.capcuch@gmail.com", "+420600000003", channels, null, null, "Europe/Prague");

        // Act — mutating the caller's list must not leak into the constructed preference.
        channels.Add(ChannelType.Sms);

        // Assert
        preference.EnabledChannels.Should().Equal(ChannelType.Email);
    }
}
