using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Auth;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// Composition root for the BFF infrastructure layer: observability, the outbound
/// <c>client_credentials</c> service-auth host (ADR-0010), the redis-cache FusionCache, the Catalog +
/// Inventory typed clients, and health checks.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddBffObservability(isDeployedEnvironment, configuration)
            .AddServiceAuth("bff")
            .AddBffCache(configuration)
            .AddCatalogClient(configuration)
            .AddInventoryClient(configuration)
            .AddBffHealthChecks();

        return services;
    }
}
