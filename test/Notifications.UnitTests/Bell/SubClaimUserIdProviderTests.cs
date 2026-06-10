using System.Security.Claims;
using AwesomeAssertions;
using Notifications.Api.SignalRHubs;
using Xunit;

namespace Notifications.UnitTests.Bell;

/// <summary>
/// Unit coverage for the bell hub's recipient-id resolution — the per-user group key. Exercises all
/// three branches: the Keycloak <c>sub</c> claim, the <see cref="ClaimTypes.NameIdentifier"/>
/// fallback (when <c>MapInboundClaims</c> has rewritten <c>sub</c>), and the no-identity case.
/// </summary>
public sealed class SubClaimUserIdProviderTests
{
    [Fact]
    public void ResolveRecipientId_PrefersTheSubClaim()
    {
        var sub = Guid.CreateVersion7().ToString();
        var principal = PrincipalWith(
            new Claim("sub", sub),
            new Claim(ClaimTypes.NameIdentifier, "a-different-value"));

        SubClaimUserIdProvider.ResolveRecipientId(principal).Should().Be(sub);
    }

    [Fact]
    public void ResolveRecipientId_FallsBackToNameIdentifier_WhenSubIsAbsent()
    {
        var nameId = Guid.CreateVersion7().ToString();
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, nameId));

        SubClaimUserIdProvider.ResolveRecipientId(principal).Should().Be(nameId);
    }

    [Fact]
    public void ResolveRecipientId_ReturnsNull_WhenNeitherClaimIsPresent()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.Name, "no-identity@dotnetatlas.test"));

        SubClaimUserIdProvider.ResolveRecipientId(principal).Should().BeNull();
    }

    [Fact]
    public void ResolveRecipientId_ReturnsNull_ForANullPrincipal()
    {
        SubClaimUserIdProvider.ResolveRecipientId(null).Should().BeNull();
    }

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
}
