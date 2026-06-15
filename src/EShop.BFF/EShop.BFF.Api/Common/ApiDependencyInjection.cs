namespace EShop.BFF.Api.Common;

/// <summary>
/// Wires the API layer for the BFF: FastEndpoints + Swagger and ProblemDetails. The endpoints in this
/// slice are public (bff.md § 3.1), so no authentication is configured here yet; inbound JWT auth +
/// scope policies land with the first authenticated endpoint (basket / order-summary).
/// </summary>
internal static class ApiDependencyInjection
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddBffFastEndpoints(configuration);

        services.AddProblemDetails();

        // No inbound JWT auth in this slice (the endpoint is anonymous), but authorization
        // services are still required: they provide the IAuthorizationPolicyProvider the Swagger
        // document processor resolves. Full JWT auth + scope policies land with the first
        // authenticated endpoint.
        services.AddAuthorization();

        return services;
    }
}
