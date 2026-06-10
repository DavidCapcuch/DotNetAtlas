using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Bell;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Domain.Templates;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using NSubstitute;
using Platform.SharedKernel.Exceptions;
using Xunit;

namespace Notifications.IntegrationTests.Dispatch;

/// <summary>
/// Dispatcher-direct integration coverage for the bell channel (ADR-0032 § 3, #317): the real
/// <c>BellChannelDispatcher</c> against a real <see cref="NotificationsDbContext"/>, bypassing
/// Hangfire, asserting the push on the fixture's broadcaster substitute. The bell is ephemeral —
/// the happy path proves the <i>absence</i> of the durable-channel contract (no
/// <c>notification_deliveries</c> row, no delivery event) alongside the rendered push. No
/// preference row is arranged anywhere: the SignalR group key IS <c>RecipientUserId</c>, so the
/// bell needs no recipient resolution.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class BellChannelDispatcherTests : BaseIntegrationTest
{
    private readonly IntegrationTestFixture _fixture;

    public BellChannelDispatcherTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
        _fixture.ResetBroadcasterSubstitute();
    }

    [Fact]
    public async Task Dispatch_RendersBodyFromDbTemplate_PushesToRecipient_NoLedgerRow_NoDeliveryEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedBellTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await DispatchViaKeyedAsync(dispatch, ct);

        // BellNotification is a record — value equality pins the fully-rendered body.
        await _fixture.BroadcasterSubstitute.Received(1).PushToUserAsync(
            recipientUserId,
            new BellNotification("Order ORD-2026-000042 has shipped."),
            Arg.Any<CancellationToken>());

        (await LedgerRowExistsAsync(notificationId, ct)).Should().BeFalse(
            "the bell is ephemeral — no (NotificationId, Bell) ledger row (ADR-0032 § 2)");

        _fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_NoBellTemplateChannel_Throws_AndDoesNotPush()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately arrange nothing — the (TemplateKey, Bell) row is absent (producer named an
        // unknown template, or one without bell content). Bug-class: fail before any push.
        var dispatch = BuildDispatch(Guid.CreateVersion7(), Guid.CreateVersion7());

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Bell);
            await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        await _fixture.BroadcasterSubstitute.DidNotReceive().PushToUserAsync(
            Arg.Any<Guid>(), Arg.Any<BellNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_PayloadMissingTemplateToken_Throws_AndDoesNotPush()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedBellTemplateAsync(ct);
        // Payload omits OrderNumber — the dispatcher must loud-fail rather than push a literal
        // "{{OrderNumber}}" bell (the email/SMS dispatchers' shared guard).
        var dispatch = new NotificationDispatch
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = Guid.CreateVersion7(),
            TemplateKey = "order.shipped",
            Payload = new Dictionary<string, string> { ["TrackingUrl"] = "https://shipping.example.com/x" },
        };

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Bell);
            await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        await _fixture.BroadcasterSubstitute.DidNotReceive().PushToUserAsync(
            Arg.Any<Guid>(), Arg.Any<BellNotification>(), Arg.Any<CancellationToken>());
    }

    private static NotificationDispatch BuildDispatch(Guid notificationId, Guid recipientUserId) => new()
    {
        NotificationId = notificationId,
        RecipientUserId = recipientUserId,
        TemplateKey = "order.shipped",
        Payload = new Dictionary<string, string>
        {
            ["OrderNumber"] = "ORD-2026-000042",
        },
    };

    private async Task DispatchViaKeyedAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Bell);
        await dispatcher.DispatchAsync(dispatch, ct);
    }

    private async Task ArrangeOrderShippedBellTemplateAsync(CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed for order.shipped → Bell.
        await using var scope = _fixture.CreateScope();
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

    private async Task<bool> LedgerRowExistsAsync(Guid notificationId, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.NotificationDeliveries.AnyAsync(d => d.NotificationId == notificationId, ct);
    }
}
