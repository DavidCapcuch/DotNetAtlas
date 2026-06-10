using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Notifications.Api.SignalRHubs;

/// <summary>
/// Resolves SignalR's <see cref="HubCallerContext.UserIdentifier"/> from the Keycloak <c>sub</c>
/// claim (falling back to <see cref="ClaimTypes.NameIdentifier"/>), so the bell hub keys its
/// per-user groups by RecipientUserId. SignalR's built-in <see cref="DefaultUserIdProvider"/>
/// reads only <see cref="ClaimTypes.NameIdentifier"/>, to which Keycloak's <c>sub</c> is not
/// reliably aliased — mirrors the Basket BC's <c>GetUserIdFromSubClaim</c> convention (ADR-0010).
/// </summary>
internal sealed class SubClaimUserIdProvider : IUserIdProvider
{
    private const string SubClaimType = "sub";

    public string? GetUserId(HubConnectionContext connection) => ResolveRecipientId(connection.User);

    /// <summary>
    /// Resolves the recipient id from a principal: the <c>sub</c> claim, or
    /// <see cref="ClaimTypes.NameIdentifier"/> when <c>MapInboundClaims</c> has rewritten <c>sub</c>
    /// to it; <c>null</c> when the principal carries neither (an unauthenticated / malformed token,
    /// which the hub's <c>[Authorize]</c> gate and its own guard then reject).
    /// </summary>
    internal static string? ResolveRecipientId(ClaimsPrincipal? user)
        => user?.FindFirstValue(SubClaimType)
           ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
}
