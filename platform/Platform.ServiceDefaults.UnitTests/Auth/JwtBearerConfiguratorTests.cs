using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

/// <summary>
/// Pins the Keycloak flat-<c>roles</c> claim contract at the platform layer (#234), so it is
/// owned by the component that owns the behaviour (<see cref="JwtBearerConfigurator"/>) rather
/// than by a single BC's functional suite.
///
/// <para>
/// Production Keycloak access tokens carry realm roles in the flat <c>roles</c> array claim only
/// (the <c>roles-flat</c> oidc-usermodel-realm-role-mapper in <c>src/keycloak/realm-export.json</c>);
/// they do NOT carry the <see cref="ClaimTypes.Role"/> URI claim that ASP.NET's
/// <see cref="ClaimsPrincipal.IsInRole"/> reads by default. Admin authorization works across every
/// BC consuming <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/> only because
/// <see cref="JwtBearerOptions.MapInboundClaims"/> stays <c>true</c> and the
/// <see cref="JsonWebTokenHandler"/> inbound map rewrites <c>roles</c> → <see cref="ClaimTypes.Role"/>
/// during validation, while <c>RoleClaimType</c> stays at its default. If a future change disables
/// the mapping or overrides <c>RoleClaimType</c> to <c>"roles"</c>, admin auth would break in
/// production for EVERY role-gated BC (Catalog, Inventory, Payments, Ordering, Invoicing) at once —
/// these tests fail loudly first.
/// </para>
/// </summary>
public class JwtBearerConfiguratorTests
{
    private const string TestAudience = "platform-tests-service";

    [Fact]
    public async Task AddPlatformJwtBearer_TokenWithOnlyFlatKeycloakRolesClaim_SatisfiesIsInRole()
    {
        // Arrange — sign a token whose ONLY role claim is the flat Keycloak "roles" claim
        // (no ClaimTypes.Role URI claim), exactly the production shape.
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "platform-test-key-1" };
        var token = SignToken(
            signingKey,
            new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
            new Claim("roles", "admin"));

        var options = BuildPlatformJwtBearerOptions();
        // No Authority/JWKS in a unit test — pin the signer directly so the (re-pinned to true)
        // ValidateIssuerSigningKey / RequireSignedTokens flags validate against a known key.
        options.TokenValidationParameters.IssuerSigningKey = signingKey;

        // Act — validate exactly as JwtBearerHandler does: a JsonWebTokenHandler whose
        // MapInboundClaims is taken from the configured options.
        var validator = new JsonWebTokenHandler { MapInboundClaims = options.MapInboundClaims };
        var result = await validator.ValidateTokenAsync(token, options.TokenValidationParameters);

        // Assert
        using var _ = new AssertionScope();
        result.IsValid.Should().BeTrue("the platform-signed flat-roles token must validate");
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        principal.IsInRole("admin").Should().BeTrue(
            "a Keycloak-shape token with only the flat \"roles\" claim must satisfy IsInRole(\"admin\") " +
            "— a false here means JwtBearerConfigurator disabled MapInboundClaims or overrode " +
            "RoleClaimType, which would break admin auth across every role-gated BC (#234)");
    }

    [Fact]
    public void AddPlatformJwtBearer_KeepsInboundClaimMappingAndDefaultRoleClaimType()
    {
        // Fast canary on the two knobs the behavioural test depends on, with a pointer to the
        // exact lines to fix if either drifts.
        var options = BuildPlatformJwtBearerOptions();

        using var _ = new AssertionScope();
        options.MapInboundClaims.Should().BeTrue(
            "JwtBearerConfigurator must not disable inbound claim mapping — Keycloak's flat \"roles\" " +
            "claim is rewritten to ClaimTypes.Role only while this is true (#234)");
        options.TokenValidationParameters.RoleClaimType.Should().Be(
            ClaimTypes.Role,
            "RoleClaimType must stay at its default — setting it to \"roles\" tells IsInRole to look " +
            "for a claim the inbound mapping has already renamed (#234, JwtBearerConfigurator.cs)");
    }

    [Fact]
    [Trait("Category", "security")]
    public void AddPlatformJwtBearer_WhenConfigureDelegateDisablesSignatureValidation_FloorRePinsItTrue()
    {
        // The #223 immutable floor: even if a BC's configuration.Bind (simulated here by the
        // configure delegate) flips the signed-token / signing-key validation booleans off, the
        // platform PostConfigure re-pins them to true — a typo'd appsettings override cannot silently
        // disable signature validation on a deployed host. (ValidateIssuer/Audience/Lifetime are
        // re-pinned by the same floor but cannot be probed here without tripping the CA5404 analyzer;
        // the flat-roles token-validation test above exercises them through a real validation.)
        var options = BuildPlatformJwtBearerOptions(o =>
        {
            o.TokenValidationParameters.RequireSignedTokens = false;
            o.TokenValidationParameters.ValidateIssuerSigningKey = false;
        });

        using var _ = new AssertionScope();
        options.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
        options.TokenValidationParameters.ValidateIssuerSigningKey.Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "security")]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Production", true)]
    public void AddPlatformJwtBearer_DefaultsRequireHttpsMetadata_ToDeployedTiersOnly(
        string environmentName,
        bool expectedRequireHttpsMetadata)
    {
        // The default is keyed on IsDeployedEnvironment() = !(IsDevelopment() || IsTesting()), NOT on
        // !IsDevelopment(): Testing is a non-deployed tier that talks to a local http Keycloak (or a
        // cleared authority + FakeTokenSigner), so requiring HTTPS metadata there pairs a `true` flag
        // with the http:// Authority in base appsettings — a combination the framework's own
        // JwtBearerPostConfigureOptions rejects the moment ValidateOnStart materializes the options at
        // boot. Each case kills a distinct mutant: dropping either disjunct of the env-gate, or
        // inverting the default outright.

        // Act
        var options = BuildPlatformJwtBearerOptions(environmentName: environmentName);

        // Assert
        options.RequireHttpsMetadata.Should().Be(expectedRequireHttpsMetadata);
    }

    [Fact]
    [Trait("Category", "security")]
    public void AddPlatformJwtBearer_DoesNotSeedInboundTrustAnchorFromServiceAuthOptions()
    {
        // The inbound token-validation trust anchor (Authority + ValidIssuer) is "whose tokens do I
        // accept". ServiceAuthOptions.Authority is a DIFFERENT concern — "which Keycloak do I fetch MY
        // OWN client-credentials token from" (outbound). The platform must never derive the first from
        // the second: a BC that binds no inbound Authority (the deployment-shaped base that ships no
        // dev-only http Authority) must NOT silently inherit the outbound realm as its inbound anchor.
        // The non-deployed (Development) tier keeps the deployed ValidateOnStart guards no-op so the
        // seeding — not a guard throw — is what this test observes; the deployed fail-closed consequence
        // is pinned by JwtBearerDeployedGuardBootTests.
        const string outboundAuthority = "https://outbound.example/realms/some-other-realm";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(
            new StubHostEnvironment { EnvironmentName = Environments.Development });
        services.Configure<ServiceAuthOptions>(o =>
        {
            o.Authority = outboundAuthority;
            o.ClientId = "platform-tests";
            o.ClientSecret = "dev-secret";
            o.ServiceName = TestAudience;
        });

        // The BC configure delegate pins only the inbound audience — it binds NO inbound Authority,
        // standing in for a base appsettings that ships no Authority key.
        services.AddPlatformJwtBearer(o => o.TokenValidationParameters.ValidAudience = TestAudience);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        using var _ = new AssertionScope();
        options.Authority.Should().BeNull(
            "inbound Authority must come from Authentication:JwtBearer, not the outbound ServiceAuth realm");
        options.TokenValidationParameters.ValidIssuer.Should().BeNull(
            "ValidIssuer must not be derived from ServiceAuthOptions.Authority — issuer validation leans " +
            "on the OIDC discovery issuer via the Authority-built ConfigurationManager");
    }

    private static JwtBearerOptions BuildPlatformJwtBearerOptions(
        Action<JwtBearerOptions>? extraConfigure = null,
        string? environmentName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(
            new StubHostEnvironment { EnvironmentName = environmentName ?? Environments.Production });

        // SUT — the BC's configure delegate stands in for the appsettings bind: it supplies the inbound
        // Authority + audience (the platform no longer seeds them from ServiceAuthOptions). ValidIssuer
        // is pinned here to stand in for the OIDC discovery issuer a live Authority's ConfigurationManager
        // would supply — a unit test has no Keycloak to fetch it from, and the token below is signed with
        // the same issuer. extraConfigure lets a test simulate a hostile bind (e.g. flipping validation
        // booleans off).
        services.AddPlatformJwtBearer(o =>
        {
            o.Authority = StubHostEnvironment.Issuer;
            o.TokenValidationParameters.ValidAudience = TestAudience;
            o.TokenValidationParameters.ValidIssuer = StubHostEnvironment.Issuer;
            extraConfigure?.Invoke(o);
        });

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static string SignToken(SecurityKey signingKey, params Claim[] claims)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = StubHostEnvironment.Issuer,
            Audience = TestAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        // HTTPS so the configurator's RequireHttpsMetadata (= IsDeployedEnvironment) is satisfied.
        public const string Issuer = "https://tests.dotnetatlas.local/realms/dotnetatlas";

        public string ApplicationName { get; set; } = "Platform.ServiceDefaults.UnitTests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
