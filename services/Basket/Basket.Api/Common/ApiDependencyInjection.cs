using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Idempotency;

namespace Basket.Api.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the presentation layer for Basket: FastEndpoints + Swagger, CORS, ProblemDetails,
    /// and the idempotency-key output cache (ADR-0013, backed by <c>redis-cache</c>).
    /// Authentication + the outbound service-auth host registration live in
    /// <see cref="AuthenticationDependencyInjection"/> and are wired explicitly from Program.cs.
    /// </summary>
    public static IServiceCollection AddPresentation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddBasketFastEndpoints();

        services.AddBasketCors(configuration, environment);

        services.AddProblemDetails();

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "basket");

        return services;
    }
}
