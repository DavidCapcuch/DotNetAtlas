using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Bell;
using Notifications.IntegrationTests.Common;
using Notifications.IntegrationTests.Common.TestClientInfrastructure;

namespace Notifications.IntegrationTests.Bell;

/// <summary>
/// Slice coverage for the bell transport (#316), entered through its public entrance — a real
/// SignalR client over the TestServer's WebSocket: an authenticated client connects, auto-joins its
/// per-user group, and receives a server-side <see cref="INotificationBroadcaster"/> push — plus the
/// auth gate, group isolation, and the zero-connection no-op.
/// </summary>
[Collection<IntegrationTestCollection>]
public class NotificationBroadcastHubTests : BaseIntegrationTest
{
    private readonly INotificationBroadcaster _broadcaster;

    public NotificationBroadcastHubTests(IntegrationTestFixture app)
        : base(app)
    {
        _broadcaster = Scope.ServiceProvider.GetRequiredService<INotificationBroadcaster>();
    }

    [Fact]
    public async Task AuthenticatedClient_AutoJoinsItsUserGroup_AndReceivesBroadcast()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        await using var client = await SignalRClientFactory.ConnectAsAsync(userId);

        // Arrives only if the authenticated client auto-joined its user group.
        var received = await NotificationHubProbe.PushUntilReceivedAsync(
            _broadcaster, userId, client, new BellNotification("ping"), ct);

        received.Message.Should().Be("ping");
    }

    // The production dotnetatlas-swagger client stamps a MULTI-VALUED `aud` (bff + the role-only Ordering /
    // Invoicing admin audiences + notifications-service), so a real browser token reaches the bell with more
    // than one `aud` entry — not the single-valued audience the other tests mint. ValidateAudience is
    // any-match, so the bell (ValidAudience = "notifications-service") must accept such a token as long as its
    // own audience is one of the entries. This pins fix-(a): the swagger audience mapper is only useful if the
    // hub accepts the multi-aud shape it produces.
    private static readonly string[] SwaggerStyleAudiences =
    [
        "bff", "ordering-service", "invoicing-service", "notifications-service"
    ];

    [Fact]
    [Trait("Category", "security")]
    public async Task ClientWithMultiValuedAudienceContainingNotificationsService_Connects_AndReceivesBroadcast()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        await using var client = await SignalRClientFactory.ConnectWithAudiencesAsync(userId, SwaggerStyleAudiences);

        // Arrives only if the bell accepted the multi-valued aud and the client joined its group.
        var received = await NotificationHubProbe.PushUntilReceivedAsync(
            _broadcaster, userId, client, new BellNotification("ping"), ct);

        received.Message.Should().Be("ping");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task ClientWithValidAudienceButNoSubjectClaim_IsConnectedThenDroppedByTheHub()
    {
        // A token can carry aud=notifications-service yet still be unusable by the bell if it has no
        // `sub` — the hub keys its per-user group on `sub` (SubClaimUserIdProvider) and aborts a
        // connection it cannot resolve to a recipient. The token IS authenticated, so [Authorize]
        // passes and the handshake completes; the hub then drops the connection in OnConnectedAsync.
        // This is exactly the shape a Keycloak client with NO subject mapper issues into its ACCESS
        // token; the dotnetatlas-swagger realm fix adds a `subject` mapper so a real dev login carries
        // `sub`. Pins that requirement end-to-end.
        await using var client = await SignalRClientFactory.ConnectSubjectlessAsync(SwaggerStyleAudiences);

        var dropped = await client.WaitUntilClosedAsync(TimeSpan.FromSeconds(5));

        dropped.Should().BeTrue(
            "a token with the right audience but no sub authenticates yet has no recipient identity, so the hub drops the connection");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task ClientWhoseMultiValuedAudienceOmitsNotificationsService_IsRejected()
    {
        // A token audienced for other BCs but NOT notifications-service must not reach the bell —
        // proves the hub's ValidAudience pin actually discriminates (the negative of the test above),
        // so a token minted for a different resource can never connect.
        var userId = Guid.CreateVersion7();
        string[] otherBcAudiences = ["basket-service", "catalog-service", "ordering-service"];

        // Pinned to the 401, not any exception: a connect that merely timed out must not pass as a rejection.
        await SignalRClientFactory
            .Invoking(factory => factory.ConnectWithAudiencesAsync(userId, otherBcAudiences))
            .Should()
            .ThrowAsync<HttpRequestException>()
            .Where(
                e => e.StatusCode == HttpStatusCode.Unauthorized,
                "the bell pins aud=notifications-service; a token audienced only for other BCs must be rejected");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Broadcast_IsScopedToTheRecipientGroup_OtherUsersDoNotReceiveIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipientId = Guid.CreateVersion7();
        var bystanderId = Guid.CreateVersion7();
        await using var recipient = await SignalRClientFactory.ConnectAsAsync(recipientId);
        await using var bystander = await SignalRClientFactory.ConnectAsAsync(bystanderId);
        // The bystander must already be in its own group, or "received nothing" proves nothing.
        await NotificationHubProbe.ProveGroupJoinedAsync(_broadcaster, bystanderId, bystander, ct);

        await NotificationHubProbe.PushUntilReceivedAsync(
            _broadcaster, recipientId, recipient, new BellNotification("for-recipient"), ct);

        (await NotificationHubProbe.ReceivedBeforeSentinelAsync(_broadcaster, bystanderId, bystander, ct))
            .Should().BeEmpty("a different user must not receive another user's bell push");
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task UnauthenticatedClient_IsRejected()
    {
        await SignalRClientFactory.Invoking(factory => factory.ConnectUnauthenticatedAsync())
            .Should()
            .ThrowAsync<HttpRequestException>()
            .Where(
                e => e.StatusCode == HttpStatusCode.Unauthorized,
                "the hub is [Authorize]-gated and the connection carries no token");
    }

    [Fact]
    public async Task PushToUserWithNoLiveConnection_IsASuccessfulNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var offlineUserId = Guid.CreateVersion7();

        await _broadcaster
            .Invoking(b => b.PushToUserAsync(offlineUserId, new BellNotification("into-the-void"), ct))
            .Should()
            .NotThrowAsync("a group-send to zero connections is a successful no-op — the bell is ephemeral");
    }
}
