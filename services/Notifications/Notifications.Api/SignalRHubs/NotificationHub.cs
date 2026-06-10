using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Notifications.Api.Common.Constants;
using Notifications.Application.Bell;

namespace Notifications.Api.SignalRHubs;

/// <summary>
/// SignalR hub for the in-app notification bell (#316, ADR-0032). JWT-authenticated; every
/// connection auto-joins a group keyed by its <see cref="HubCallerContext.UserIdentifier"/>
/// (= Keycloak <c>sub</c> = RecipientUserId) on connect and leaves it on disconnect, so a
/// server-side push to that group reaches exactly the recipient's live connections. The bell is
/// ephemeral: no client-to-server RPC, no persistence, no replay — a push to zero connections is a
/// successful no-op (notifications.md § 6).
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub<INotificationClientContract>
{
    /// <summary>Versioned hub route: <c>/hubs/v1/notifications</c>.</summary>
    public const string RoutePattern = $"{BasePaths.HubsBasePath}/v1/notifications";

    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // [Authorize] guarantees an authenticated principal and SubClaimUserIdProvider a non-null
        // UserIdentifier (the `sub`). Guard defensively: a null here means the auth / user-id
        // pipeline is misconfigured, so fail the connection rather than silently join group "".
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError(
                "Bell connection {ConnectionId} has no UserIdentifier; aborting (auth/user-id pipeline misconfigured).",
                Context.ConnectionId);
            throw new HubException("Connection has no user identity.");
        }

        // No CancellationToken: a join cancelled mid-flight (e.g. over a future Redis backplane,
        // ADR-0016) would abort the connection while leaving it ungrouped. Let the fast join
        // complete; OnDisconnectedAsync removes the membership if the client then drops.
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);

        _logger.LogInformation(
            "User {UserIdentifier} connection {ConnectionId} connected and joined its bell group.",
            userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        }

        _logger.LogInformation(
            "User {UserIdentifier} connection {ConnectionId} disconnected and left its bell group.",
            userId, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }
}
