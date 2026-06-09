using AwesomeAssertions;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Xunit;

namespace Notifications.UnitTests.Preferences;

/// <summary>
/// <see cref="NotificationPreference"/> is seeded reference data (notifications.md § 8) with no runtime
/// mutation surface; the only behaviour to guard is construction — chiefly the both-or-neither quiet-hours
/// invariant (a half-specified window is meaningless to the quiet-hours scheduler, #315).
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
        using var _ = new AssertionScope();
        preference.UserId.Should().Be(UserId);
        preference.Email.Should().Be("d.capcuch@gmail.com");
        preference.EnabledChannels.Should().Equal(ChannelType.Email, ChannelType.Sms, ChannelType.Bell);
        preference.QuietHoursStart.Should().BeNull();
        preference.QuietHoursEnd.Should().BeNull();
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
        using var _ = new AssertionScope();
        preference.QuietHoursStart.Should().Be(new TimeOnly(22, 0));
        preference.QuietHoursEnd.Should().Be(new TimeOnly(7, 0));
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
