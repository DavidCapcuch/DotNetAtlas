using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// Inbound-side helper for validating OAuth2 bearer tokens (ADR-0010). Wires an
/// <see cref="AuthenticationBuilder"/> with defaults derived from
/// <see cref="ServiceAuthOptions"/>; callers override via the optional configure delegate.
/// </summary>
public static class JwtBearerConfigurator
{
    /// <summary>
    /// Registers authentication and a JWT-bearer scheme with:
    /// <list type="bullet">
    /// <item><description><c>Authority</c> = <see cref="ServiceAuthOptions.Authority"/></description></item>
    /// <item><description><c>ValidIssuer</c> = <see cref="ServiceAuthOptions.Authority"/></description></item>
    /// <item><description><c>ValidateIssuer</c> / <c>ValidateAudience</c> / <c>ValidateLifetime</c> = <c>true</c></description></item>
    /// <item><description><c>ClockSkew</c> = 5 minutes per ADR-0010</description></item>
    /// </list>
    /// <c>ValidAudience</c> is intentionally <b>not</b> defaulted here — each BC must pin it
    /// explicitly under <c>Authentication:JwtBearer:TokenValidationParameters:ValidAudience</c>
    /// in <c>appsettings.json</c>. The BC's <paramref name="configure"/> callback binds that
    /// section in step 2 below.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three-phase contract (defense-in-depth):
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Configure</b> seeds <see cref="JwtBearerOptions"/> from
    /// <see cref="ServiceAuthOptions"/> (Authority, ValidIssuer = Authority, all five
    /// validation booleans <c>true</c>, ClockSkew per ADR-0010). <c>ValidAudience</c> is
    /// left at its <see cref="TokenValidationParameters"/> default (<c>null</c>) so the
    /// BC's appsettings is the single source of truth.
    /// </description></item>
    /// <item><description>
    /// The BC's <paramref name="configure"/> callback runs inside this Configure
    /// step. BCs typically <c>configuration.Bind("Authentication:JwtBearer", options)</c>
    /// here, which can override <b>any</b> field — including silently flipping a
    /// validation boolean to <c>false</c> via a typo'd env var or a malformed
    /// appsettings override. The bind is also where <c>ValidAudience</c> arrives from
    /// the BC's <c>Authentication:JwtBearer:TokenValidationParameters:ValidAudience</c>
    /// key; if a BC forgets to set it, <c>ValidateAudience=true</c> + <c>ValidAudience=null</c>
    /// rejects every token at runtime (fails closed).
    /// </description></item>
    /// <item><description>
    /// <b>PostConfigure</b> runs <i>after</i> the BC's <c>configuration.Bind</c>
    /// (and any other Configure callback) and re-pins the five security-critical
    /// booleans (<c>ValidateIssuer / ValidateAudience / ValidateLifetime /
    /// ValidateIssuerSigningKey / RequireSignedTokens</c>) to <c>true</c>. This
    /// is the immutable security floor — no appsettings, env var, or BC-specific
    /// override can opt out of validation, per #223.
    /// </description></item>
    /// </list>
    /// <para>
    /// Net result: the <c>ValidAudience</c> / <c>ValidIssuer</c> <i>strings</i>
    /// are configurable per BC (so a BC can validate against multiple audiences,
    /// migrate authorities, etc.), but the boolean "are we validating at all"
    /// flags are non-negotiable.
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
            .Configure<IOptions<ServiceAuthOptions>>((jwt, serviceAuth) =>
            {
                var opts = serviceAuth.Value;
                jwt.Authority = opts.Authority;
                jwt.RequireHttpsMetadata = !string.Equals(
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    "Development",
                    StringComparison.OrdinalIgnoreCase);
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
                // The contract is pinned by Ordering.FunctionalTests
                // MarkOrderShippedTests.WhenTokenCarriesOnlyKeycloakFlatRolesClaim_AdminAuthSucceeds.
                // If a future ASP.NET Core flips MapInboundClaims=false by
                // default, or someone overrides it here, that test fails
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
                    ValidIssuer = opts.Authority,
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

        return builder;
    }
}
