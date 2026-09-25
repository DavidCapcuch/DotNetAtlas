using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Bell;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Domain.Templates;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using Notifications.IntegrationTests.Common.TestClientInfrastructure;

namespace Notifications.IntegrationTests.Dispatch;

/// <summary>
/// The bell channel (ADR-0032 § 3, #317) entered through its ephemeral dispatch job: the bug-class
/// guards fail before anything is pushed, and an offline recipient is a successful no-op. The happy
/// path is the journey test's (<c>Journeys/OrderShippedNotificationJourneyTests</c>).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class BellChannelDispatcherTests : BaseIntegrationTest
{
    private readonly INotificationBroadcaster _broadcaster;

    public BellChannelDispatcherTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _broadcaster = Scope.ServiceProvider.GetRequiredService<INotificationBroadcaster>();
    }

    [Fact]
    public async Task Dispatch_NoBellTemplateChannel_Throws_AndDoesNotPush()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately arrange nothing — the (TemplateKey, Bell) row is absent (producer named an
        // unknown template, or one without bell content). Bug-class: fail before any push.
        var recipientUserId = Guid.CreateVersion7();
        var dispatch = BuildDispatch(recipientUserId);
        await using var client = await ConnectAndProveJoinedAsync(recipientUserId, ct);

        await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Bell, dispatch, "Notifications.MissingBellTemplateChannel", ct);

        (await NotificationHubProbe.ReceivedBeforeSentinelAsync(_broadcaster, recipientUserId, client, ct))
            .Should().BeEmpty("a missing bell template must fail before anything reaches the recipient's group");
    }

    [Fact]
    public async Task Dispatch_PayloadMissingTemplateToken_Throws_AndDoesNotPush()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedBellTemplateAsync(ct);
        // Payload omits OrderNumber — the dispatcher must loud-fail rather than push a literal
        // "{{OrderNumber}}" bell (the email/SMS dispatchers' shared guard).
        var recipientUserId = Guid.CreateVersion7();
        var dispatch = new NotificationDispatch
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            TemplateKey = "order.shipped",
            Payload = new Dictionary<string, string> { ["TrackingUrl"] = "https://shipping.example.com/x" },
        };
        await using var client = await ConnectAndProveJoinedAsync(recipientUserId, ct);

        var exception = await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Bell, dispatch, "Notifications.UnresolvedTemplateTokens", ct);

        using (new AssertionScope())
        {
            exception.Message.Should().Contain("OrderNumber");
            (await NotificationHubProbe.ReceivedBeforeSentinelAsync(_broadcaster, recipientUserId, client, ct))
                .Should().BeEmpty("an unresolved template token must fail before anything reaches the recipient's group");
        }
    }

    [Fact]
    public async Task Dispatch_RecipientHasNoLiveConnection_IsASuccessfulNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedBellTemplateAsync(ct);
        var dispatch = BuildDispatch(Guid.CreateVersion7());

        // No client connects — offline = missed, by design (ADR-0032): a group-send to zero
        // connections must complete rather than fail the job into a retry.
        var act = () => Fixture.RunDispatchJobAsync(ChannelType.Bell, dispatch, ct);

        await act.Should().NotThrowAsync();
    }

    private static NotificationDispatch BuildDispatch(Guid recipientUserId) => new()
    {
        NotificationId = Guid.CreateVersion7(),
        RecipientUserId = recipientUserId,
        TemplateKey = "order.shipped",
        Payload = new Dictionary<string, string>
        {
            ["OrderNumber"] = "ORD-2026-000042",
        },
    };

    private async Task<NotificationHubTestClient> ConnectAndProveJoinedAsync(Guid recipientUserId, CancellationToken ct)
    {
        var client = await SignalRClientFactory.ConnectAsAsync(recipientUserId);
        try
        {
            await NotificationHubProbe.ProveGroupJoinedAsync(_broadcaster, recipientUserId, client, ct);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private async Task ArrangeOrderShippedBellTemplateAsync(CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed for order.shipped → Bell.
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
}
