using EShop.BFF.Api.Composition;

namespace EShop.BFF.Api.Common;

/// <summary>
/// Wires the API layer for the BFF: FastEndpoints + Swagger, ProblemDetails, and the composition
/// providers. The endpoints in these slices are public (bff.md § 3.1 / § 3.4), so no authentication is
/// configured here yet; inbound JWT auth + scope policies land with the first authenticated endpoint
/// (basket / order-summary).
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

        // No inbound JWT auth in this slice (the endpoint is anonymous), but authorization
        // services are still required: they provide the IAuthorizationPolicyProvider the Swagger
        // document processor resolves. Full JWT auth + scope policies land with the first
        // authenticated endpoint.
        services.AddAuthorization();

        return services;
    }
}
