using Microsoft.AspNetCore.Authentication.JwtBearer;
using Notifications.Api.Common.Constants;
using Platform.ServiceDefaults.Auth;

namespace Notifications.Api.Common;

/// <summary>
/// Authentication + authorization wiring for the Notifications API. The BC is headless (a Kafka
/// consumer plus the in-app bell SignalR hub) with no UI surface, so — like Ordering / Catalog /
/// Invoicing — it wires JWT bearer only (no Cookie / OIDC schemes). Uses the platform
/// <see cref="JwtBearerConfigurator.AddPlatformJwtBearer"/> so the Keycloak flat-<c>roles</c>
/// mapping, the immutable validation floor (#223), and the deployed HTTPS-metadata guard are
/// centralised; the BC pins its audience via the <c>Authentication:JwtBearer</c> section in
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
    /// The deployed-environment JWT hardening — fail-closed at host boot when
    /// <c>RequireHttpsMetadata</c> is off — is owned by the platform
    /// <see cref="JwtBearerConfigurator"/> and applies to every inbound-JWT edge uniformly; there is
    /// no Notifications-specific auth guard (ADR-0009 item 10).
    /// </remarks>
    public static IServiceCollection AddNotificationsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
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

        services.AddAuthorization();
        services.AddHttpContextAccessor();

        return services;
    }
}
