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

    private static JwtBearerOptions BuildPlatformJwtBearerOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());
        // Authority drives ValidIssuer; reuse it as the token issuer below.
        services.Configure<ServiceAuthOptions>(o =>
        {
            o.Authority = StubHostEnvironment.Issuer;
            o.ClientId = "platform-tests";
            o.ClientSecret = "dev-secret";
            o.ServiceName = TestAudience;
        });

        // SUT — the BC's configure delegate pins ValidAudience, exactly as appsettings binding does.
        services.AddPlatformJwtBearer(o => o.TokenValidationParameters.ValidAudience = TestAudience);

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
        // HTTPS so the configurator's RequireHttpsMetadata (= !IsDevelopment) is satisfied.
        public const string Issuer = "https://tests.dotnetatlas.local/realms/dotnetatlas";

        public string ApplicationName { get; set; } = "Platform.ServiceDefaults.UnitTests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
