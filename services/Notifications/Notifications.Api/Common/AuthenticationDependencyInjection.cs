using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Notifications.Api.Common.Constants;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Auth;

namespace Notifications.Api.Common;

/// <summary>
/// Authentication + authorization wiring for the Notifications API. The BC is headless (a Kafka
/// consumer plus the in-app bell SignalR hub) with no UI surface, so — like Ordering / Catalog /
/// Invoicing — it wires JWT bearer only (no Cookie / OIDC schemes). Uses the platform
/// <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/> so the Keycloak flat-<c>roles</c>
/// mapping, the immutable validation floor (#223), and the per-environment HTTPS-metadata toggle
/// are centralised; the BC pins its audience via the <c>Authentication:JwtBearer</c> section in
/// appsettings (ADR-0010).
/// </summary>
internal static class AuthenticationDependencyInjection
{
    private const string JwtBearerConfigSection = "Authentication:JwtBearer";

    /// <summary>
    /// Registers JWT bearer authentication (via the platform configurator) for the bell hub, plus
    /// the SignalR <c>access_token</c>-from-query-string path scoped to <see cref="BasePaths.HubsBasePath"/>
    /// (rationale at the event handler below). The hub authorises any authenticated user — the bell
    /// is per-user, not role-gated — so no role policy is registered. Notifications has no outbound
    /// HTTP calls, so the outbound service-auth host (<c>AddServiceAuth</c>) is intentionally not
    /// wired (no <c>ServiceAuth</c> section in appsettings).
    /// </summary>
    /// <remarks>
    /// In <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> environments a
    /// post-configure guard asserts <c>RequireSignedTokens</c> and <c>ValidateIssuerSigningKey</c>
    /// remain enabled — protects against a misconfigured env-var silently relaxing JWT validation
    /// in production. HTTPS-metadata gating is handled by <see cref="JwtBearerConfigurator"/> based
    /// on <c>ASPNETCORE_ENVIRONMENT</c>.
    /// </remarks>
    public static IServiceCollection AddNotificationsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPlatformJwtBearer(options =>
        {
            configuration.Bind(JwtBearerConfigSection, options);

            // Browsers cannot set the Authorization header on a WebSocket handshake, so the SignalR
            // client sends the access token in the query string. Scope the lift to the hub base
            // path so non-hub requests keep using the Authorization header only.
            // See https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.HttpContext.Request.Path.StartsWithSegments(
                            BasePaths.HubsBasePath, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = context.Request.Query["access_token"];
                    }

                    return Task.CompletedTask;
                }
            };
        });

        if (environment.IsDeployedEnvironment())
        {
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .PostConfigure(options =>
                {
                    if (!options.TokenValidationParameters.RequireSignedTokens
                        || !options.TokenValidationParameters.ValidateIssuerSigningKey)
                    {
                        throw new InvalidOperationException(
                            "JWT validation must require signed tokens and validate the signing " +
                            "key in deployed environments. Check 'Authentication:JwtBearer' " +
                            "configuration overrides.");
                    }
                });
        }

        services.AddAuthorization();
        services.AddHttpContextAccessor();

        return services;
    }
}
