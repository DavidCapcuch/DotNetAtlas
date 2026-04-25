using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Composition root for the Inventory Infrastructure layer. Called from
/// <c>Inventory.API.Program.cs</c> after <c>AddApplication</c>. Wires the
/// persistence slice (DbContext, EF Core, event-store repository) and the
/// messaging slice (KafkaFlow cluster + 3 consumers + transactional outbox
/// + inbox dedup). Health checks land in M7 alongside the admin HTTP
/// endpoints — no <c>AddInventoryHealthChecks</c> in M5.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddMessaging(configuration);

        return services;
    }
}
