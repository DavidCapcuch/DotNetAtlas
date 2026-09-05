using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Clients.Basket;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;
using EShop.BFF.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Auth;
using Platform.ServiceDefaults.FeatureFlags;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// Composition root for the BFF infrastructure layer: observability, the outbound
/// <c>client_credentials</c> service-auth host + the RFC 8693 user-token-exchange host (ADR-0010), the
/// redis-cache FusionCache, the Catalog + Inventory (service-token) and Basket (token-exchange) typed
/// clients, feature flags (ADR-0014), the <c>bff-group</c> Kafka cache invalidator, and health checks.
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
            .AddUserTokenExchange()
            .AddFeatureFlags(configuration)
            .AddBffCache(configuration)
            .AddCatalogClient(configuration)
            .AddInventoryClient(configuration)
            .AddBasketClient(configuration)
            .AddBasketWriteClient()
            .AddBffMessaging(configuration)
            .AddBffHealthChecks(configuration);

        return services;
    }
}
