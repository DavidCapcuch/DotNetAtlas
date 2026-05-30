using Platform.ServiceDefaults.Idempotency;

namespace Basket.Api.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the API layer for Basket: FastEndpoints + Swagger, CORS, ProblemDetails,
    /// and the idempotency-key output cache (ADR-0013, backed by <c>redis-cache</c>).
    /// Authentication + the outbound service-auth host registration live in
    /// <see cref="AuthenticationDependencyInjection"/> and are wired explicitly from Program.cs.
    /// CORS invariants are enforced at startup by <c>BasketCorsOptionsValidator</c>.
    /// </summary>
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddBasketFastEndpoints(configuration);

        services.AddBasketCors(configuration);

        services.AddProblemDetails();

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "basket-service");

        services.AddRazorPages();

        return services;
    }
}
