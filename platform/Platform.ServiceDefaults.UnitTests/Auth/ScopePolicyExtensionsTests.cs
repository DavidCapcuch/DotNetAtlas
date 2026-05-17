using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

public class ScopePolicyExtensionsTests
{
    [Fact]
    public void RequireScope_BuildsPolicy_WithScopeClaimRequirement()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireScope("catalog.read")
            .Build();

        policy.Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Should().ContainSingle(r =>
                r.ClaimType == ScopePolicyExtensions.ScopeClaimType &&
                r.AllowedValues!.Contains("catalog.read"));
    }

    [Fact]
    public async Task RequireScope_AuthorizationHandler_SucceedsWhenScopeMatches()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services
            .AddAuthorizationBuilder()
            .AddPolicy("test", p => p.RequireScope("catalog.read"))
            .Services
            .BuildServiceProvider();
        var svc = sp.GetRequiredService<IAuthorizationService>();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[] { new Claim(ScopePolicyExtensions.ScopeClaimType, "catalog.read") },
            authenticationType: "Bearer"));

        var result = await svc.AuthorizeAsync(principal, resource: null, policyName: "test");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireScope_AuthorizationHandler_FailsWhenScopeMissing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services
            .AddAuthorizationBuilder()
            .AddPolicy("test", p => p.RequireScope("catalog.read"))
            .Services
            .BuildServiceProvider();
        var svc = sp.GetRequiredService<IAuthorizationService>();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[] { new Claim(ScopePolicyExtensions.ScopeClaimType, "catalog.write") },
            authenticationType: "Bearer"));

        var result = await svc.AuthorizeAsync(principal, resource: null, policyName: "test");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireScope_AuthorizationHandler_FailsWhenUnauthenticated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services
            .AddAuthorizationBuilder()
            .AddPolicy("test", p => p.RequireScope("catalog.read"))
            .Services
            .BuildServiceProvider();
        var svc = sp.GetRequiredService<IAuthorizationService>();

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await svc.AuthorizeAsync(anonymous, resource: null, policyName: "test");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RequireScope_NullScope_Throws()
    {
        var builder = new AuthorizationPolicyBuilder();
        builder.Invoking(b => b.RequireScope(null!)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireScope_EmptyScope_Throws()
    {
        var builder = new AuthorizationPolicyBuilder();
        builder.Invoking(b => b.RequireScope(string.Empty)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireScope_WhitespaceScope_Throws()
    {
        var builder = new AuthorizationPolicyBuilder();
        builder.Invoking(b => b.RequireScope("   ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequireScope_NullBuilder_Throws()
    {
        AuthorizationPolicyBuilder builder = null!;
        Action act = () => builder.RequireScope("catalog.read");
        act.Should().Throw<ArgumentNullException>();
    }
}
