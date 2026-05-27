using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// Main orchestrator for infrastructure dependencies.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddOpenTelemetry(isDeployedEnvironment, configuration)
            .AddNotificationsHealthChecks(configuration);

        services
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddKafkaMessaging(configuration);

        return services;
    }
}
