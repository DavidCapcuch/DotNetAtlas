using AwesomeAssertions;
using Notifications.Domain.Channels;
using Notifications.Infrastructure.Persistence.Database.Seed;
using Xunit;

namespace Notifications.UnitTests.Persistence;

/// <summary>
/// The seeded recipient preferences encode the demo's resolution variety (notifications.md § 8): the four
/// Keycloak realm subs, pleb with Sms suppressed, and d.capcuch's Europe/Prague quiet-hours window. These
/// are the seed's <i>point</i>, so they are guarded against a typo silently breaking the demo.
/// </summary>
public sealed class DatabaseSeedPreferencesTests
{
    private static readonly Guid AdminSub = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DevSub = Guid.Parse("00000000-0000-0000-0000-111111111111");
    private static readonly Guid PlebSub = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DCapcuchSub = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void BuildSeedPreferences_SeedsExactlyTheFourRealmUsers()
    {
        var preferences = DatabaseSeedExtensions.BuildSeedPreferences();

        preferences.Select(p => p.UserId)
            .Should().BeEquivalentTo([AdminSub, DevSub, PlebSub, DCapcuchSub]);
    }

    [Fact]
    public void BuildSeedPreferences_UsesRealEmailsForRecognizableMailpitRecipients()
    {
        var preferences = DatabaseSeedExtensions.BuildSeedPreferences();

        preferences.Single(p => p.UserId == DCapcuchSub).Email.Should().Be("d.capcuch@gmail.com");
        preferences.Single(p => p.UserId == AdminSub).Email.Should().Be("admin@dotnetatlas.com");
    }

    [Fact]
    public void BuildSeedPreferences_PlebHasSmsDisabled()
    {
        var preferences = DatabaseSeedExtensions.BuildSeedPreferences();

        var pleb = preferences.Single(p => p.UserId == PlebSub);
        pleb.EnabledChannels.Should().NotContain(ChannelType.Sms);
        pleb.EnabledChannels.Should().Contain([ChannelType.Email, ChannelType.Bell]);
    }

    [Fact]
    public void BuildSeedPreferences_DCapcuchHasPragueQuietHours()
    {
        var preferences = DatabaseSeedExtensions.BuildSeedPreferences();

        var dCapcuch = preferences.Single(p => p.UserId == DCapcuchSub);
        using (new AssertionScope())
        {
            dCapcuch.QuietHoursStart.Should().Be(new TimeOnly(22, 0));
            dCapcuch.QuietHoursEnd.Should().Be(new TimeOnly(7, 0));
            dCapcuch.TimeZone.Should().Be("Europe/Prague");
            dCapcuch.EnabledChannels.Should().Contain(ChannelType.Sms);
        }
    }

    [Fact]
    public void BuildSeedPreferences_AdminAndDevHaveNoQuietHours()
    {
        var preferences = DatabaseSeedExtensions.BuildSeedPreferences();

        using (new AssertionScope())
        {
            foreach (var sub in new[] { AdminSub, DevSub })
            {
                var preference = preferences.Single(p => p.UserId == sub);
                preference.QuietHoursStart.Should().BeNull();
                preference.QuietHoursEnd.Should().BeNull();
                preference.EnabledChannels.Should().Contain([ChannelType.Email, ChannelType.Sms, ChannelType.Bell]);
            }
        }
    }
}
