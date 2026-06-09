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
using Platform.SharedKernel.Exceptions;
using Xunit;

namespace Notifications.IntegrationTests.Dispatch;

/// <summary>
/// Dispatcher-direct integration coverage for the fake SMS channel (ADR-0032 § 3, #315): the real
/// <see cref="SmsChannelDispatcher"/> against a real <see cref="NotificationsDbContext"/>, bypassing
/// Hangfire. The log line is the channel's only transport, so the happy path asserts it alongside
/// the shared durable-channel contract — the <c>(NotificationId, Sms)</c> ledger row and the delivery
/// event on the fixture's outbox substitute. The transient-failure UPSERT branch is the email
/// dispatcher's covered contract (#312); the fake send cannot fail, so it has no SMS-side test.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class SmsChannelDispatcherTests : BaseIntegrationTest
{
    private const string NotifyEventsTopic = "notifications.notify-events";

    private readonly IntegrationTestFixture _fixture;

    public SmsChannelDispatcherTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Dispatch_RendersBodyFromDbTemplate_LogsTheSend_RecordsDispatchedLedger_AndEmitsDispatchedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedSmsTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, "+420600000042", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);
        var logger = new CollectingLogger<SmsChannelDispatcher>();

        await using (var scope = _fixture.CreateScope())
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

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
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
        await ArrangePreferenceAsync(recipientUserId, "+420600000042", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await DispatchViaKeyedAsync(dispatch, ct);
        await DispatchViaKeyedAsync(dispatch, ct); // ledger already Dispatched → skip

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
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

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Sms);
            await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        _fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_PayloadMissingTemplateToken_Throws_AndEmitsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedSmsTemplateAsync(ct);
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, "+420600000042", ct);
        // Payload omits TrackingUrl — the dispatcher must loud-fail rather than log a literal
        // "{{TrackingUrl}}" SMS and record Dispatched (the email dispatcher's shared guard).
        var dispatch = new NotificationDispatch
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            TemplateKey = "order.shipped",
            Payload = new Dictionary<string, string> { ["OrderNumber"] = "ORD-2026-000007" },
        };

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Sms);
            await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        _fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
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

    private async Task DispatchViaKeyedAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Sms);
        await dispatcher.DispatchAsync(dispatch, ct);
    }

    private SmsChannelDispatcher BuildDispatcher(AsyncServiceScope scope, CollectingLogger<SmsChannelDispatcher> logger)
    {
        var sp = scope.ServiceProvider;
        return new SmsChannelDispatcher(
            sp.GetRequiredService<INotificationsDbContext>(),
            _fixture.OutboxSubstitute,
            sp.GetRequiredService<IRecipientResolver>(),
            sp.GetRequiredService<IOptions<TopicsOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            logger);
    }

    private async Task ArrangeOrderShippedSmsTemplateAsync(CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed for order.shipped → Sms.
        await using var scope = _fixture.CreateScope();
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
        await using var scope = _fixture.CreateScope();
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
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var row = await db.NotificationDeliveries.SingleAsync(
            d => d.NotificationId == notificationId && d.Channel == ChannelType.Sms, ct);
        return row.Status;
    }
}
