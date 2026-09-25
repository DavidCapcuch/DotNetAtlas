using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using Xunit;

namespace Notifications.IntegrationTests.Persistence;

/// <summary>
/// Round-trips <see cref="NotificationPreference"/> through real PostgreSQL across its two non-default
/// EF mappings (notifications.md § 8): the <c>IReadOnlyList&lt;ChannelType&gt; ↔ text[]</c>
/// <c>ValueConverter</c>/<c>ValueComparer</c> and the <c>TimeOnly? → time</c> quiet-hours columns.
/// Reloads in a fresh scope so the assertion reads the database, not the change tracker.
/// </summary>
/// <remarks>
/// The fan-out handler reads these columns (<c>NotifyUserCommandKafkaHandlerTests</c>), but only
/// through what it resolves from them; this is where every column is asserted as written, including an
/// empty channel list and null quiet hours, which the handler cannot tell apart from missing ones.
/// Nothing in production writes <c>user_preferences</c> (rows arrive only via Development seeding), so
/// the write half has no outer entrance — fold this into a slice test once a preference write
/// endpoint exists.
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class NotificationPreferencePersistenceTests : BaseIntegrationTest
{
    public NotificationPreferencePersistenceTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Preference_RoundTripsEnabledChannelsAndQuietHours()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        await PersistAsync(
            NotificationPreference.Create(
                userId,
                email: "buyer@dotnetatlas.test",
                phoneNumber: "+420600000000",
                enabledChannels: [ChannelType.Bell, ChannelType.Email], // intentionally not value-ordered
                quietHoursStart: new TimeOnly(22, 0),
                quietHoursEnd: new TimeOnly(7, 0),
                timeZone: "Europe/Prague"),
            ct);

        var reloaded = await ReloadAsync(userId, ct);

        using (new AssertionScope())
        {
            // text[] preserves element order, and FromName rehydrates each SmartEnum.
            reloaded.EnabledChannels.Should().Equal(ChannelType.Bell, ChannelType.Email);
            reloaded.QuietHoursStart.Should().Be(new TimeOnly(22, 0));
            reloaded.QuietHoursEnd.Should().Be(new TimeOnly(7, 0));
            reloaded.TimeZone.Should().Be("Europe/Prague");
        }
    }

    [Fact]
    public async Task Preference_RoundTripsEmptyChannelsAndNullQuietHours()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        await PersistAsync(
            NotificationPreference.Create(
                userId,
                email: "buyer@dotnetatlas.test",
                phoneNumber: "+420600000000",
                enabledChannels: [], // disabled every channel — the load-bearing empty-array path
                quietHoursStart: null,
                quietHoursEnd: null,
                timeZone: "Europe/Prague"),
            ct);

        var reloaded = await ReloadAsync(userId, ct);

        using (new AssertionScope())
        {
            reloaded.EnabledChannels.Should().BeEmpty();
            reloaded.QuietHoursStart.Should().BeNull();
            reloaded.QuietHoursEnd.Should().BeNull();
        }
    }

    private async Task PersistAsync(NotificationPreference preference, CancellationToken ct)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.UserPreferences.Add(preference);
        await db.SaveChangesAsync(ct);
    }

    private async Task<NotificationPreference> ReloadAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.UserPreferences.AsNoTracking().SingleAsync(p => p.UserId == userId, ct);
    }
}
