using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Bell;
using Notifications.FunctionalTests.Common;
using Notifications.FunctionalTests.Common.TestClientInfrastructure;

namespace Notifications.FunctionalTests.Bell;

/// <summary>
/// End-to-end functional tests for the bell transport (#316): an authenticated client connects,
/// auto-joins its per-user group, and receives a server-side <see cref="INotificationBroadcaster"/>
/// push — plus the auth gate, group isolation, and the zero-connection no-op.
/// </summary>
[Collection<FunctionalTestCollection>]
public class NotificationBroadcastHubTests : BaseApiTest
{
    private static readonly TimeSpan ExpectNoMessageTimeout = TimeSpan.FromMilliseconds(500);

    private readonly INotificationBroadcaster _broadcaster;

    public NotificationBroadcastHubTests(ApiTestFixture app)
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

        var received = await PushUntilReceivedAsync(userId, client, new BellNotification("ping"), ct);

        using (new AssertionScope())
        {
            received.Should().NotBeNull("the authenticated client auto-joins its user group and receives the push");
            received!.Message.Should().Be("ping");
        }
    }

    [Fact]
    public async Task Broadcast_IsScopedToTheRecipientGroup_OtherUsersDoNotReceiveIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var recipientId = Guid.CreateVersion7();
        var bystanderId = Guid.CreateVersion7();
        await using var recipient = await SignalRClientFactory.ConnectAsAsync(recipientId);
        await using var bystander = await SignalRClientFactory.ConnectAsAsync(bystanderId);

        var recipientReceived = await PushUntilReceivedAsync(
            recipientId, recipient, new BellNotification("for-recipient"), ct);
        var bystanderReceived = await bystander.ConsumeOne(ExpectNoMessageTimeout, ct);

        using (new AssertionScope())
        {
            recipientReceived.Should().NotBeNull("the push targets the recipient's user group");
            bystanderReceived.Should().BeNull("a different user must not receive another user's bell push");
        }
    }

    [Fact]
    public async Task UnauthenticatedClient_IsRejected()
    {
        await SignalRClientFactory.Invoking(factory => factory.ConnectUnauthenticatedAsync())
            .Should()
            .ThrowAsync<Exception>("the hub is [Authorize]-gated and the connection carries no token");
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

    /// <summary>
    /// Pushes to <paramref name="userId"/>'s group and waits for the first delivery, retrying within a
    /// bounded budget. SignalR runs the hub's <c>OnConnectedAsync</c> (the group auto-join) shortly
    /// <i>after</i> the client's <c>StartAsync</c> returns, so the first push can race the join;
    /// retrying makes the test deterministic without an artificial sleep.
    /// </summary>
    private async Task<BellNotification?> PushUntilReceivedAsync(
        Guid userId, NotificationHubTestClient client, BellNotification payload, CancellationToken ct)
    {
        const int attempts = 10;
        var perAttempt = TimeSpan.FromMilliseconds(200);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await _broadcaster.PushToUserAsync(userId, payload, ct);

            var received = await client.ConsumeOne(perAttempt, ct);
            if (received is not null)
            {
                return received;
            }
        }

        return null;
    }
}
