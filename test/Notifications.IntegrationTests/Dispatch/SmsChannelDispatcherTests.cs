using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notifications.Application.Common.Data;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Dispatch;
using Notifications.Application.Recipients;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using Notifications.Infrastructure.Dispatch;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using NSubstitute;
using Xunit;

namespace Notifications.IntegrationTests.Dispatch;

/// <summary>
/// The fake SMS channel (ADR-0032 § 3, #315) entered through its durable dispatch job, against a real
/// <see cref="NotificationsDbContext"/>. The log line is the channel's only transport, so the happy
/// path asserts it alongside the shared durable-channel contract — the <c>(NotificationId, Sms)</c>
/// ledger row and the delivery event on the fixture's outbox substitute. The transient-failure UPSERT
/// branch is the email dispatcher's covered contract (#312); the fake send cannot fail, so it has no
/// SMS-side test.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class SmsChannelDispatcherTests : BaseIntegrationTest
{
    private const string NotifyEventsTopic = "notifications.notify-events";

    public SmsChannelDispatcherTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Dispatch_RendersBodyFromDbTemplate_LogsTheSend_RecordsDispatchedLedger_AndEmitsDispatchedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedSmsTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, phoneNumber: "+420600000042", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);
        var logger = new CollectingLogger<SmsChannelDispatcher>();

        // Constructed directly rather than through the job: the log line IS this channel's transport,
        // and the fixture has no seam to inject a per-test ILogger into the host container.
        await using (var scope = Fixture.CreateScope())
        {
            var dispatcher = BuildDispatcher(scope, logger);
            await dispatcher.DispatchAsync(dispatch, ct);
        }

        // The phone number resolved from user_preferences (#315) and the fully-rendered body are in
        // the log line — the fake channel's entire transport.
        logger.Messages.Should().ContainSingle(m =>
            m.Contains("+420600000042")
            && m.Contains("Your order ORD-2026-000007 shipped. Track: https://shipping.example.com/ORD-2026-000007"));

        (await LoadLedgerStatusAsync(notificationId, ct)).Should().Be(DeliveryStatus.Dispatched);

        Fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            recipientUserId.ToString(),
            Arg.Is<NotificationDeliveryStatusChangedEvent>(e =>
                e.NotificationId == notificationId
                && e.RecipientUserId == recipientUserId
                && e.Channel == "Sms"
                && e.Status == NotificationDeliveryStatus.Dispatched));
    }

    [Fact]
    public async Task Dispatch_Redelivered_DoesNotDispatchTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedSmsTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, phoneNumber: "+420600000042", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await Fixture.RunDispatchJobAsync(ChannelType.Sms, dispatch, ct);
        await Fixture.RunDispatchJobAsync(ChannelType.Sms, dispatch, ct); // ledger already Dispatched → skip

        Fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            Arg.Any<string>(),
            Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_NoSmsTemplateChannel_Throws_AndEmitsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately arrange nothing — the (TemplateKey, Sms) row is absent (producer named an
        // unknown template, or one without SMS content). Bug-class: fail before any send or write.
        var dispatch = BuildDispatch(Guid.CreateVersion7(), Guid.CreateVersion7());

        await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Sms, dispatch, "Notifications.MissingSmsTemplateChannel", ct);

        Fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_PayloadMissingTemplateToken_Throws_AndEmitsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedSmsTemplateAsync(ct);
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, phoneNumber: "+420600000042", ct);
        // Payload omits TrackingUrl — the dispatcher must loud-fail rather than log a literal
        // "{{TrackingUrl}}" SMS and record Dispatched (the email dispatcher's shared guard).
        var dispatch = new NotificationDispatch
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            TemplateKey = "order.shipped",
            Payload = new Dictionary<string, string> { ["OrderNumber"] = "ORD-2026-000007" },
        };

        var exception = await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Sms, dispatch, "Notifications.UnresolvedTemplateTokens", ct);

        exception.Message.Should().Contain("TrackingUrl");
        Fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    private static NotificationDispatch BuildDispatch(Guid notificationId, Guid recipientUserId) => new()
    {
        NotificationId = notificationId,
        RecipientUserId = recipientUserId,
        TemplateKey = "order.shipped",
        Payload = new Dictionary<string, string>
        {
            ["OrderNumber"] = "ORD-2026-000007",
            ["TrackingUrl"] = "https://shipping.example.com/ORD-2026-000007",
        },
    };

    private SmsChannelDispatcher BuildDispatcher(AsyncServiceScope scope, CollectingLogger<SmsChannelDispatcher> logger)
    {
        var sp = scope.ServiceProvider;
        return new SmsChannelDispatcher(
            sp.GetRequiredService<INotificationsDbContext>(),
            Fixture.OutboxSubstitute,
            sp.GetRequiredService<IRecipientResolver>(),
            sp.GetRequiredService<IOptions<TopicsOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            logger);
    }

    private async Task ArrangeOrderShippedSmsTemplateAsync(CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed for order.shipped → Sms.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create(
            "order.shipped",
            "Sent to a buyer when their order ships (demonstrates multi-channel fan-out)."));
        db.TemplateChannels.Add(TemplateChannel.Create(
            "order.shipped",
            ChannelType.Sms,
            subject: null,
            body: "Your order {{OrderNumber}} shipped. Track: {{TrackingUrl}}"));
        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangePreferenceAsync(Guid recipientUserId, string phoneNumber, CancellationToken ct)
    {
        // The DB-backed recipient resolver (#314) reads the phone number from user_preferences, so
        // every send-path test must seed the recipient's row.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.UserPreferences.Add(NotificationPreference.Create(
            recipientUserId,
            email: $"user-{recipientUserId:N}@dotnetatlas.test",
            phoneNumber: phoneNumber,
            enabledChannels: [ChannelType.Sms],
            quietHoursStart: null,
            quietHoursEnd: null,
            timeZone: "Europe/Prague"));
        await db.SaveChangesAsync(ct);
    }

    private async Task<DeliveryStatus> LoadLedgerStatusAsync(Guid notificationId, CancellationToken ct)
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var row = await db.NotificationDeliveries.SingleAsync(
            d => d.NotificationId == notificationId && d.Channel == ChannelType.Sms, ct);
        return row.Status;
    }
}
