using EShop.BFF.Api.Composition;
using Platform.ServiceDefaults.Auth;

namespace EShop.BFF.Api.Common;

/// <summary>
/// Wires the API layer for the BFF: FastEndpoints + Swagger, ProblemDetails, the composition providers,
/// and inbound user-JWT authentication (ADR-0010). Public pages (product / home) stay
/// <c>AllowAnonymous</c>; the first required-auth endpoint (<c>GET /basket</c>) validates the user token
/// against <c>ValidAudience = bff</c>.
/// </summary>
internal static class ApiDependencyInjection
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddBffFastEndpoints(configuration);

        services.AddProblemDetails();

        // Shared home-page read-through orchestration (bff.md § 3.4) — used by the endpoint and the
        // eager-warm hosted service. Scoped so it composes with the request-scoped typed clients.
        services.AddScoped<HomePageProvider>();
        // NOTE: the HomePageCacheWarmer hosted service is registered last in Program.cs (after feature
        // flags) so it starts AFTER OpenFeature has initialized its provider — otherwise it would read the
        // flag before flags.json is loaded and the kill-switch could not turn the warm off.

        // Inbound user-JWT validation (ADR-0010 fail-closed contract): the user token is validated against
        // ValidAudience = bff (pinned in appsettings; the user-facing client stamps aud: bff — which is also
        // the RFC 8693 token-exchange subject-token holder audience). Required by GET /basket (the first
        // authenticated endpoint); the public pages stay AllowAnonymous. ServiceAuthOptions (Authority) is
        // bound by AddServiceAuth("bff") in the infrastructure composition root.
        services.AddPlatformJwtBearer(options => configuration.Bind("Authentication:JwtBearer", options));
        services.AddAuthorization();

        return services;
    }
}
