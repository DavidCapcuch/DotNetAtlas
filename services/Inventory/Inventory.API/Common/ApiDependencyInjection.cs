using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults.Idempotency;

namespace Inventory.API.Common;

internal static class ApiDependencyInjection
{
    /// <summary>
    /// Wires the presentation layer for Inventory: FastEndpoints + Swagger,
    /// JWT bearer auth + scope policies, CORS, ProblemDetails, and the
    /// idempotency-key output cache (ADR-0013, backed by <c>redis-cache</c>).
    /// Mirrors <c>services/Basket/Basket.Api/Common/ApiDependencyInjection.cs</c>
    /// minus <c>AddServiceAuth</c> — Inventory v1 has no outbound HTTP calls
    /// to other services, so the client-credentials token machinery is not
    /// needed.
    /// </summary>
    /// <remarks>
    /// Side-effect of skipping <c>AddServiceAuth</c>: the
    /// <c>ServiceAuth.ClientId</c> + <c>ClientSecret</c> entries in
    /// <c>appsettings.json</c> are inert — only <c>Authority</c> and
    /// <c>ServiceName</c> are consumed (by
    /// <see cref="Platform.ServiceDefaults.Auth.JwtBearerConfigurator"/> for
    /// inbound audience validation). The credential entries are pre-provisioned
    /// for the day Inventory grows an outbound HTTP client; until then they
    /// are documentation, not configuration.
    /// </remarks>
    public static IServiceCollection AddPresentation(
        this IServiceCollection services,
        ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        services.AddInventoryFastEndpoints();

        services.AddInventoryCors(configuration);

        services.AddProblemDetails();

        services.AddInventoryAuthentication(configuration, environment);

        services.AddIdempotencyKeyOutputCache(configuration, serviceName: "inventory");

        return services;
    }
}
