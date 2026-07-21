using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// Inbound-side helper for validating OAuth2 bearer tokens (ADR-0010). Wires an
/// <see cref="AuthenticationBuilder"/> with an immutable validation floor; the BC supplies its own
/// inbound trust anchor (Authority + audience) through the optional configure delegate — never
/// derived from <see cref="ServiceAuthOptions"/>, which is this service's OUTBOUND identity.
/// </summary>
public static class JwtBearerConfigurator
{
    /// <summary>
    /// Registers authentication and a JWT-bearer scheme. The platform seeds only the
    /// environment-derived <c>RequireHttpsMetadata</c>, the five validation booleans (<c>true</c>),
    /// and a 5-minute <c>ClockSkew</c> (ADR-0010). The inbound trust anchor is the BC's own:
    /// <list type="bullet">
    /// <item><description><c>Authority</c> — from the BC's <c>Authentication:JwtBearer:Authority</c>
    /// bind; <b>not</b> seeded from <see cref="ServiceAuthOptions"/> (this service's outbound identity).</description></item>
    /// <item><description><c>ValidIssuer</c> — left <c>null</c>; <c>iss</c> validates against the OIDC
    /// discovery issuer the <c>Authority</c>-built ConfigurationManager fetches.</description></item>
    /// <item><description><c>ValidAudience</c> — the BC pins it under
    /// <c>Authentication:JwtBearer:TokenValidationParameters:ValidAudience</c>; omitting it fails
    /// closed (<c>ValidateAudience=true</c> + <c>null</c> rejects every token).</description></item>
    /// </list>
    /// Deriving <c>Authority</c> / <c>ValidIssuer</c> from <see cref="ServiceAuthOptions"/> would
    /// conflate inbound trust with outbound identity, so neither is — for the same reason
    /// <c>ValidAudience</c> isn't (ADR-0010, 2026-05-27 amendment): an <c>AddServiceAuth</c> realm can
    /// never silently become an edge's inbound anchor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four-phase contract (defense-in-depth):
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Configure</b> seeds the environment-derived <c>RequireHttpsMetadata</c>, the five
    /// validation booleans (<c>true</c>), and <c>ClockSkew</c>. It reads no
    /// <see cref="ServiceAuthOptions"/>: <c>Authority</c>, <c>ValidIssuer</c>, and <c>ValidAudience</c>
    /// are all left for the BC to supply (or leave <c>null</c>).
    /// </description></item>
    /// <item><description>
    /// The BC's <paramref name="configure"/> callback runs inside this Configure step — typically
    /// <c>configuration.Bind("Authentication:JwtBearer", options)</c> — supplying <c>Authority</c>
    /// and <c>ValidAudience</c>. The bind can override <b>any</b> field, including silently flipping a
    /// validation boolean to <c>false</c> via a typo'd env var; phase 3 re-pins those.
    /// </description></item>
    /// <item><description>
    /// <b>PostConfigure</b> re-pins the five security-critical booleans (<c>ValidateIssuer /
    /// ValidateAudience / ValidateLifetime / ValidateIssuerSigningKey / RequireSignedTokens</c>) to
    /// <c>true</c> after the BC's bind — the immutable security floor no appsettings, env var, or
    /// BC-specific override can opt out of, per #223.
    /// </description></item>
    /// <item><description>
    /// <b>Deployed guards</b> (<c>ValidateOnStart</c>) fail a deployed host's boot when it would
    /// fetch OIDC metadata / JWKS over plain HTTP (<c>RequireHttpsMetadata=false</c>,
    /// <see cref="AssertDeployedRequireHttpsMetadata"/>) or has no inbound <c>Authority</c> at all
    /// (<see cref="AssertDeployedAuthorityConfigured"/>) — the two invariants the boolean floor
    /// cannot cover, because both are <see cref="JwtBearerOptions"/> members a <c>Bind</c> can leave
    /// at their local-dev defaults. No-op in
    /// <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/>-false tiers. See ADR-0009 item 10.
    /// </description></item>
    /// </list>
    /// <para>
    /// Net result: <c>Authority</c> / <c>ValidAudience</c> are configurable per BC (migrate realms,
    /// validate multiple audiences), issuer validation follows the realm's own discovery document,
    /// and the "are we validating at all" booleans are non-negotiable.
    /// </para>
    /// </remarks>
    /// <param name="services">The DI container.</param>
    /// <param name="configure">Optional callback to override any JwtBearerOption.</param>
    public static AuthenticationBuilder AddPlatformJwtBearer(
        this IServiceCollection services,
        Action<JwtBearerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHostEnvironment>((jwt, env) =>
            {
                // Inbound Authority + ValidIssuer are NOT seeded here — they are "whose tokens do I
                // accept", a concern the BC owns entirely through its Authentication:JwtBearer bind
                // (Authority) and the OIDC discovery issuer that Authority's ConfigurationManager
                // fetches (ValidIssuer left null). Deriving either from the outbound
                // ServiceAuthOptions.Authority — "which Keycloak do I fetch MY OWN token from" — would
                // conflate inbound trust with outbound identity and let the AddServiceAuth realm
                // silently become this edge's inbound anchor (the same fail-open the 2026-05-27
                // ADR-0010 amendment removed for ValidAudience). A deployed edge that binds no
                // Authority fails closed at boot via AssertDeployedAuthorityConfigured below.
                //
                // Non-deployed tiers (Development laptop/compose runs and the Testing fixtures, per
                // the HostEnvironmentExtensions taxonomy) reach Keycloak over plain HTTP on
                // localhost:9011 or run against a cleared authority, so the metadata-discovery
                // handshake must accept HTTP there; only deployed clusters require HTTPS metadata.
                // Keyed on IsDeployedEnvironment() rather than !IsDevelopment() so that an unbound
                // fallback still separates Testing from a deployed cluster — pairing
                // RequireHttpsMetadata=true with an http:// Authority is rejected by the framework's
                // own JwtBearerPostConfigureOptions when ValidateOnStart materializes it at boot.
                jwt.RequireHttpsMetadata = env.IsDeployedEnvironment();
                // RoleClaimType is INTENTIONALLY left at its default
                // (ClaimTypes.Role). #234 proposed setting it to "roles" to
                // match Keycloak's flat realm-role claim shape — that change
                // would have BROKEN admin auth across every BC. Reason:
                // JwtBearerOptions.MapInboundClaims defaults to true (see
                // aspnetcore JwtBearerOptions.cs — initialized from
                // JwtSecurityTokenHandler.DefaultMapInboundClaims) and the
                // InboundClaimTypeMap in Microsoft.IdentityModel.JsonWebTokens
                // contains {"roles" → ClaimTypes.Role}, so Keycloak's "roles"
                // claim is auto-rewritten to ClaimTypes.Role on the principal
                // before any authorization runs. Setting RoleClaimType="roles"
                // would tell IsInRole to look for a "roles"-typed claim that
                // the inbound mapping has already removed.
                //
                // The contract is pinned at this layer by
                // Platform.ServiceDefaults.UnitTests.Auth.JwtBearerConfiguratorTests
                // (validates a flat-"roles"-only token through these very options) and
                // end-to-end by Ordering.FunctionalTests
                // MarkOrderShippedTests.WhenTokenCarriesOnlyKeycloakFlatRolesClaim_AdminAuthSucceeds.
                // If a future ASP.NET Core flips MapInboundClaims=false by
                // default, or someone overrides it here, those tests fail
                // loudly — re-enable the mapping or wire an OnTokenValidated
                // transformer; do not set RoleClaimType.
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                };

                configure?.Invoke(jwt);
            });

        // #223: re-pin security-critical TokenValidationParameters AFTER the BC's
        // configure callback runs. The callback typically does
        // `configuration.Bind("Authentication:JwtBearer", options)`, which mutates
        // fields on the TokenValidationParameters instance the Configure step above
        // installed — so a misconfigured appsettings could silently disable
        // signed-token / signing-key / issuer / audience / lifetime validation.
        // PostConfigure runs AFTER all Configure callbacks (including the binder),
        // so it's the last word on these flags.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.RequireSignedTokens = true;
        });

        // Fourth contract phase (see the doc-comment above): the deployed RequireHttpsMetadata guard.
        // PostConfigure runs after the BC's Bind, so it observes the post-bind value; ValidateOnStart
        // forces the check at boot rather than lazily on the first authenticated request.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IHostEnvironment>(AssertDeployedRequireHttpsMetadata)
            .PostConfigure<IHostEnvironment>(AssertDeployedAuthorityConfigured)
            .ValidateOnStart();

        return builder;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a deployed host would fetch OIDC metadata
    /// / JWKS over plain HTTP (<c>RequireHttpsMetadata = false</c>) — a MITM surface on the
    /// token-validation trust anchor. No-op in Development / Testing, which run against a local http
    /// Keycloak. Registered with <c>ValidateOnStart</c> so the failure is at boot, not per request.
    /// </summary>
    private static void AssertDeployedRequireHttpsMetadata(JwtBearerOptions options, IHostEnvironment environment)
    {
        if (environment.IsDeployedEnvironment() && !options.RequireHttpsMetadata)
        {
            throw new InvalidOperationException(
                "JWT validation must require HTTPS metadata in deployed environments so OIDC " +
                "discovery and JWKS are fetched over TLS. Set " +
                "'Authentication:JwtBearer:RequireHttpsMetadata' to true and point 'Authority' at an " +
                "https:// OIDC endpoint. See ADR-0009 'Taking this to production'.");
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a deployed host has no OIDC authority to
    /// validate tokens against. Covers the one gap the framework's own metadata-address check cannot
    /// see: that check only rejects an address that is <i>present but plaintext</i>, so with neither
    /// <c>Authority</c> nor <c>MetadataAddress</c> it builds no configuration manager, the host
    /// <b>boots cleanly</b>, and every authenticated request fails afterwards instead. Reachable when
    /// an edge's <c>Authority</c> is explicitly blanked (an empty env-var override) or its appsettings
    /// omits the key — base <c>appsettings.json</c> ships a Development-tier <c>http://</c> value, so
    /// the ordinary forgot-to-override case is already caught at boot by the framework check. No-op
    /// outside deployed environments — the test fixtures' <c>ConfigureJwtBearerForTests</c>
    /// deliberately clears both values.
    /// </summary>
    /// <remarks>
    /// Only presence is asserted, not the scheme: in a deployed environment
    /// <see cref="AssertDeployedRequireHttpsMetadata"/> guarantees <c>RequireHttpsMetadata</c> is
    /// <c>true</c>, and the framework's own post-configure then rejects any non-https address. A
    /// scheme check here would be a branch that can never trip.
    /// </remarks>
    private static void AssertDeployedAuthorityConfigured(JwtBearerOptions options, IHostEnvironment environment)
    {
        if (environment.IsDeployedEnvironment()
            && string.IsNullOrWhiteSpace(options.Authority)
            && string.IsNullOrWhiteSpace(options.MetadataAddress))
        {
            throw new InvalidOperationException(
                "Inbound JWT validation needs an OIDC authority in deployed environments, but " +
                "'Authentication:JwtBearer:Authority' and 'MetadataAddress' are both empty. Set " +
                "'Authentication:JwtBearer:Authority' to an https:// OIDC realm URL. See ADR-0009 " +
                "'Taking this to production'.");
        }
    }
}
