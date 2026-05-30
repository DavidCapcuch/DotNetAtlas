using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

public class ScopePolicyExtensionsTests
{
    [Fact]
    public async Task RequireAnyScope_SucceedsWhenSpaceSeparatedClaimContainsScope()
    {
        var svc = BuildAuthorizationService(p => p.RequireAnyScope("catalog.read"));

        // Realistic Keycloak shape: a single space-separated `scope` claim.
        var principal = PrincipalWithScopeClaims("openid profile catalog.read");

        var result = await svc.AuthorizeAsync(principal, resource: null, policyName: "test");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAnyScope_SucceedsWhenAnyOfTheRequiredScopesPresent()
    {
        // read-or-write hierarchy: the write scope satisfies a read policy.
        var svc = BuildAuthorizationService(p => p.RequireAnyScope("catalog.read", "catalog.write"));

        var principal = PrincipalWithScopeClaims("openid catalog.write");

        var result = await svc.AuthorizeAsync(principal, resource: null, policyName: "test");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAnyScope_SucceedsWhenScopesSplitAcrossMultipleClaims()
    {
        var svc = BuildAuthorizationService(p => p.RequireAnyScope("catalog.write"));

        // RFC 8693-styled IdP: one `scope` claim per scope.
        var principal = PrincipalWithScopeClaims("openid", "profile", "catalog.write");

        var result = await svc.AuthorizeAsync(principal, resource: null, policyName: "test");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAnyScope_FailsWhenNoRequiredScopePresent()
    {
        var svc = BuildAuthorizationService(p => p.RequireAnyScope("catalog.read", "catalog.write"));

        var principal = PrincipalWithScopeClaims("openid profile inventory.read");

        var result = await svc.AuthorizeAsync(principal, resource: null, policyName: "test");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireAnyScope_FailsWhenUnauthenticated()
    {
        var svc = BuildAuthorizationService(p => p.RequireAnyScope("catalog.read"));

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await svc.AuthorizeAsync(anonymous, resource: null, policyName: "test");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RequireAnyScope_NoScopes_Throws()
    {
        var builder = new AuthorizationPolicyBuilder();
        builder.Invoking(b => b.RequireAnyScope()).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireAnyScope_NullOrBlankScope_Throws(string? scope)
    {
        var builder = new AuthorizationPolicyBuilder();
        builder.Invoking(b => b.RequireAnyScope("catalog.read", scope!))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireAnyScope_NullBuilder_Throws()
    {
        AuthorizationPolicyBuilder builder = null!;
        Action act = () => builder.RequireAnyScope("catalog.read");
        act.Should().Throw<ArgumentNullException>();
    }

    private static IAuthorizationService BuildAuthorizationService(Action<AuthorizationPolicyBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services
            .AddAuthorizationBuilder()
            .AddPolicy("test", configure)
            .Services
            .BuildServiceProvider();
        return sp.GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal PrincipalWithScopeClaims(params string[] scopeClaimValues)
    {
        var claims = scopeClaimValues
            .Select(value => new Claim(ScopePolicyExtensions.ScopeClaimType, value));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }
}
