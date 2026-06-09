using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Recipients;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using Platform.SharedKernel.Exceptions;
using Xunit;

namespace Notifications.IntegrationTests.Recipients;

/// <summary>
/// Integration coverage for the DB-backed recipient resolver (notifications.md § 8): the real
/// <see cref="IRecipientResolver"/> resolves the email address from the seeded <c>user_preferences</c>
/// table — replacing the #312 synthetic-email stub — and loud-fails on a missing row.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class DbRecipientResolverTests : BaseIntegrationTest
{
    private readonly IntegrationTestFixture _fixture;

    public DbRecipientResolverTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ResolveAsync_ReturnsTheEmailFromUserPreferences()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(userId, "invoice-buyer@dotnetatlas.test", ct);

        await using var scope = _fixture.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRecipientResolver>();

        var contact = await resolver.ResolveAsync(userId, ct);

        contact.Email.Should().Be("invoice-buyer@dotnetatlas.test");
    }

    [Fact]
    public async Task ResolveAsync_WhenNoPreferenceRow_ThrowsDataIntegrityException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRecipientResolver>();

        var act = () => resolver.ResolveAsync(Guid.CreateVersion7(), ct);

        await act.Should().ThrowAsync<DataIntegrityException>();
    }

    private async Task ArrangePreferenceAsync(Guid userId, string email, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.UserPreferences.Add(NotificationPreference.Create(
            userId,
            email,
            phoneNumber: "+420600000000",
            enabledChannels: [ChannelType.Email],
            quietHoursStart: null,
            quietHoursEnd: null,
            timeZone: "Europe/Prague"));
        await db.SaveChangesAsync(ct);
    }
}
