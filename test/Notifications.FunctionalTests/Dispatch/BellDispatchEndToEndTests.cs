using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.Bell;
using Notifications.Application.Common.Data;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using Notifications.FunctionalTests.Common;
using Notifications.FunctionalTests.Common.TestClientInfrastructure;
using Notifications.Infrastructure.NotifyUser;
using Notifications.Infrastructure.Persistence.Database;

namespace Notifications.FunctionalTests.Dispatch;

/// <summary>
/// End-to-end functional coverage for the bell channel (#317, AC 5): a real
/// <see cref="NotifyUserCommandKafkaHandler"/> fan-out for <c>order.shipped</c>, drained through the
/// real job + keyed <c>BellChannelDispatcher</c> + broadcaster + hub, observed by the ported SignalR
/// test client. Only Hangfire's queue mechanics are replaced
/// (<see cref="RecordingChannelDispatchEnqueuer"/>). Every arrangement deliberately excludes the
/// Email channel — this fixture runs no Mailpit, so a drained email job would hit a dead SMTP host.
/// </summary>
[Collection<FunctionalTestCollection>]
public class BellDispatchEndToEndTests : BaseApiTest
{
    private static readonly TimeSpan ExpectNoMessageTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ApiTestFixture _app;
    private readonly INotificationBroadcaster _broadcaster;
    private readonly RecordingChannelDispatchEnqueuer _enqueuer = new();

    public BellDispatchEndToEndTests(ApiTestFixture app)
        : base(app)
    {
        _app = app;
        _broadcaster = Scope.ServiceProvider.GetRequiredService<INotificationBroadcaster>();
    }

    [Fact]
    public async Task OrderShipped_UserWithBellEnabled_ConnectedClientReceivesTheRenderedPush()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipientUserId = Guid.CreateVersion7();
        await ArrangeOrderShippedTemplateAsync(withSmsChannel: false, ct);
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Bell], ct);
        await using var client = await SignalRClientFactory.ConnectAsAsync(recipientUserId);
        await ProveGroupJoinedAsync(recipientUserId, client, ct);

        await HandleOrderShippedAsync(Guid.CreateVersion7(), recipientUserId, ct);
        await _enqueuer.DrainAsync(_app.Services, ct);

        var received = await client.ConsumeOne(TimeSpan.FromSeconds(2), ct);
        using (new AssertionScope())
        {
            received.Should().NotBeNull("the fan-out resolved Bell and the dispatcher pushed to the recipient's group");
            received!.Message.Should().Be("Order ORD-2026-000042 has shipped.");
        }
    }

    [Fact]
    public async Task OrderShipped_UserWithBellDisabled_ConnectedClientReceivesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipientUserId = Guid.CreateVersion7();
        var notificationId = Guid.CreateVersion7();
        await ArrangeOrderShippedTemplateAsync(withSmsChannel: true, ct);
        // Bell disabled while another supported channel stays on — the intersection must suppress
        // exactly the bell, not the whole fan-out.
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Sms], ct);
        await using var client = await SignalRClientFactory.ConnectAsAsync(recipientUserId);
        // Prove the group join completed BEFORE running the pipeline — otherwise "received nothing"
        // could false-pass on a client that simply had not joined its group yet.
        await ProveGroupJoinedAsync(recipientUserId, client, ct);

        await HandleOrderShippedAsync(notificationId, recipientUserId, ct);
        await _enqueuer.DrainAsync(_app.Services, ct);

        var received = await client.ConsumeOne(ExpectNoMessageTimeout, ct);
        using (new AssertionScope())
        {
            received.Should().BeNull("the recipient disabled Bell, so resolution must exclude it");
            _enqueuer.RecordedChannels.Should().Equal([ChannelType.Sms], "only the enabled Sms channel resolves");
            (await SmsLedgerRowExistsAsync(notificationId, ct)).Should().BeTrue(
                "the drained Sms job proves the fan-out ran end-to-end and only the bell was suppressed");
        }
    }

    [Fact]
    public async Task OrderShipped_UserWithBellEnabled_ButNoLiveConnection_IsASuccessfulNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipientUserId = Guid.CreateVersion7();
        await ArrangeOrderShippedTemplateAsync(withSmsChannel: false, ct);
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Bell], ct);

        // No client connects — offline = missed, by design (ADR-0032). The pipeline must complete
        // without error: a group-send to zero connections is a successful no-op.
        var act = async () =>
        {
            await HandleOrderShippedAsync(Guid.CreateVersion7(), recipientUserId, ct);
            await _enqueuer.DrainAsync(_app.Services, ct);
        };

        await act.Should().NotThrowAsync();
        _enqueuer.RecordedChannels.Should().Equal([ChannelType.Bell], "the bell job must really have run for the no-op to mean anything");
    }

    /// <summary>
    /// Pushes probes until the client observes one: SignalR runs the hub's <c>OnConnectedAsync</c>
    /// (the group auto-join) shortly <i>after</i> the client's <c>StartAsync</c> returns, so dispatch
    /// pushes could race the join. Afterwards flushes any duplicate probes that landed outside their
    /// wait window, so later assertions never consume a stale probe.
    /// </summary>
    private async Task ProveGroupJoinedAsync(Guid userId, NotificationHubTestClient client, CancellationToken ct)
    {
        const int attempts = 10;
        var probe = new BellNotification("group-join-probe");

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await _broadcaster.PushToUserAsync(userId, probe, ct);
            if (await client.ConsumeOne(TimeSpan.FromMilliseconds(200), ct) is not null)
            {
                while (await client.ConsumeOne(TimeSpan.FromMilliseconds(100), ct) is not null)
                {
                }

                return;
            }
        }

        throw new InvalidOperationException("SignalR client never observed the group-join probe push.");
    }

    private async Task HandleOrderShippedAsync(Guid notificationId, Guid recipientUserId, CancellationToken ct)
    {
        // The real handler, constructed with the recording enqueuer — the integration handler-test
        // pattern; the consumer (Kafka) and the job queue (Hangfire) are the only fakes in the path.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<INotificationsDbContext>();
        var handler = new NotifyUserCommandKafkaHandler(
            db,
            _enqueuer,
            TimeProvider.System,
            NullLogger<NotifyUserCommandKafkaHandler>.Instance);

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

        await handler.Handle(TestKafkaMessageContext.Create(ct), cmd);
    }

    private async Task ArrangeOrderShippedTemplateAsync(bool withSmsChannel, CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed bodies; never an Email row in this fixture.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create(
            "order.shipped",
            "Sent to a buyer when their order ships (demonstrates multi-channel fan-out)."));
        db.TemplateChannels.Add(TemplateChannel.Create(
            "order.shipped",
            ChannelType.Bell,
            subject: null,
            body: "Order {{OrderNumber}} has shipped."));
        if (withSmsChannel)
        {
            db.TemplateChannels.Add(TemplateChannel.Create(
                "order.shipped",
                ChannelType.Sms,
                subject: null,
                body: "Your order {{OrderNumber}} shipped. Track: {{TrackingUrl}}"));
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangePreferenceAsync(
        Guid recipientUserId,
        IReadOnlyList<ChannelType> enabledChannels,
        CancellationToken ct)
    {
        await using var scope = _app.Services.CreateAsyncScope();
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

    private async Task<bool> SmsLedgerRowExistsAsync(Guid notificationId, CancellationToken ct)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.NotificationDeliveries.AnyAsync(
            d => d.NotificationId == notificationId && d.Channel == ChannelType.Sms, ct);
    }
}
