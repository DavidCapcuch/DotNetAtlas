using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Auth;
using Platform.ServiceDefaults.Idempotency;

namespace Basket.Api.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the presentation layer for Basket: FastEndpoints + Swagger, JWT bearer auth,
    /// CORS, ProblemDetails, the idempotency-key output cache (ADR-0013, backed by
    /// <c>redis-cache</c>), and the outbound service-auth host registration (ADR-0010).
    /// Mirrors the shape of <c>src/Weather.Api/Common/ApiDependencyInjection.cs</c> but
    /// drops UI / Razor / Hangfire / SignalR concerns — Basket is API-only.
    /// </summary>
    public static IServiceCollection AddPresentation(
        this IServiceCollection services,
        ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        services.AddBasketFastEndpoints();

        services.AddBasketCors(configuration);

        services.AddProblemDetails();

        services.AddBasketAuthentication(configuration, environment);

        services.AddServiceAuth(serviceName: "basket");

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "basket");

        return services;
    }
}
