using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Bell;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using Notifications.Infrastructure.NotifyUser;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using Notifications.IntegrationTests.Common.TestClientInfrastructure;
using Platform.Test.Framework.Kafka;

namespace Notifications.IntegrationTests.Journeys;

/// <summary>
/// The <c>order.shipped</c> journey: the DI-registered <see cref="NotifyUserCommandKafkaHandler"/>
/// fans out, the recorded jobs drain through the real job, keyed dispatcher, broadcaster and hub, and a
/// connected bell client observes the result. Only Kafka delivery and Hangfire's queue are replaced.
/// </summary>
/// <remarks>
/// Capped at one happy path (eshop-master-design.md § 11.4); variations belong in the dispatcher and
/// handler tests. The arrangement carries Bell only, although the dev seed also maps
/// <c>order.shipped</c> to Email and Sms: those channels' contracts are covered end-to-end by their
/// own dispatcher tests, so replaying them here would duplicate rather than compose.
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class OrderShippedNotificationJourneyTests : BaseIntegrationTest
{
    private readonly INotificationBroadcaster _broadcaster;

    public OrderShippedNotificationJourneyTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _broadcaster = Scope.ServiceProvider.GetRequiredService<INotificationBroadcaster>();
    }

    [Fact]
    public async Task OrderShipped_UserWithBellEnabled_ConnectedClientReceivesTheRenderedPushOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipientUserId = Guid.CreateVersion7();
        var notificationId = Guid.CreateVersion7();
        await ArrangeOrderShippedBellTemplateAsync(ct);
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Bell], ct);
        await using var client = await SignalRClientFactory.ConnectAsAsync(recipientUserId);
        await NotificationHubProbe.ProveGroupJoinedAsync(_broadcaster, recipientUserId, client, ct);

        await HandleOrderShippedAsync(notificationId, recipientUserId, ct);
        await Fixture.DispatchEnqueuer.DrainAsync(Fixture.Services, ct);

        var received = await NotificationHubProbe.ReceivedBeforeSentinelAsync(_broadcaster, recipientUserId, client, ct);
        using (new AssertionScope())
        {
            received.Should().ContainSingle("the fan-out resolved Bell once and the dispatcher pushed it exactly once")
                .Which.Message.Should().Be("Order ORD-2026-000042 has shipped.");

            // The bell is ephemeral (ADR-0032 § 2): asserting the absence of the durable-channel
            // contract on the same run that proved the push is what makes it mean "ephemeral", not
            // "never ran".
            (await Fixture.LoadLedgerStatusAsync(notificationId, ChannelType.Bell, ct)).Should().BeNull(
                "the bell is ephemeral — no (NotificationId, Bell) ledger row (ADR-0032 § 2)");
            (await Fixture.LoadOutboxRowsAsync(ct)).Should().BeEmpty(
                "the bell is ephemeral — it records no delivery event (ADR-0032 § 4)");
        }
    }

    private async Task HandleOrderShippedAsync(Guid notificationId, Guid recipientUserId, CancellationToken ct)
    {
        await using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<NotifyUserCommandKafkaHandler>();

        var cmd = new NotifyUserCommand
        {
            NotificationId = notificationId,
            RecipientUserId = recipientUserId,
            TemplateKey = "order.shipped",
            Payload = new Dictionary<string, string>
            {
                ["OrderNumber"] = "ORD-2026-000042",
                ["TrackingUrl"] = "https://shipping.example.com/ORD-2026-000042",
            },
            OccurredOnUtc = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
        };

        await handler.Handle(FakeKafkaMessageContext.Create(cancellationToken: ct), cmd);
    }

    private async Task ArrangeOrderShippedBellTemplateAsync(CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed's bell body.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create(
            "order.shipped",
            "Sent to a buyer when their order ships (demonstrates multi-channel fan-out)."));
        db.TemplateChannels.Add(TemplateChannel.Create(
            "order.shipped",
            ChannelType.Bell,
            subject: null,
            body: "Order {{OrderNumber}} has shipped."));
        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangePreferenceAsync(
        Guid recipientUserId,
        IReadOnlyList<ChannelType> enabledChannels,
        CancellationToken ct)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.UserPreferences.Add(NotificationPreference.Create(
            recipientUserId,
            email: $"user-{recipientUserId:N}@dotnetatlas.test",
            phoneNumber: "+420600000042",
            enabledChannels: enabledChannels,
            quietHoursStart: null,
            quietHoursEnd: null,
            timeZone: "Europe/Prague"));
        await db.SaveChangesAsync(ct);
    }
}
