using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure.Common;

/// <summary>
/// Composition root for the Catalog Infrastructure layer. Called from
/// <c>Catalog.API.Program.cs</c> after <c>AddServiceDefaults</c> (correlation-id +
/// service-auth per ADR-0008/0010) and <c>AddCatalogApplication</c> (validators, CQRS
/// handlers, projection handlers, outbox publishers).
/// </summary>
/// <remarks>
/// Chains the two infrastructure slices already present from M4:
/// <list type="bullet">
/// <item><description>
/// <see cref="PersistenceDependencyInjection.AddDatabase"/> (M4.1) — <see cref="Persistence.Database.CatalogDbContext"/>
/// bound to Postgres with snake_case + exception-processor + the <c>DispatchDomainEventsInterceptor</c>
/// that fires the in-process projection write atomically with the aggregate save.
/// </description></item>
/// <item><description>
/// <see cref="MessagingDependencyInjection.AddKafkaMessaging"/> (M4.2) — KafkaFlow cluster
/// with the <c>StockLevelChanged</c> inbox consumer, transactional outbox + DLT producer,
/// and correlation-id propagation middleware.
/// </description></item>
/// </list>
/// </remarks>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddKafkaMessaging(configuration);

        return services;
    }
}
