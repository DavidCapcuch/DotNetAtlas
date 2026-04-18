using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Weather.Infrastructure.Common;

/// <summary>
/// Main orchestrator for infrastructure dependencies.
/// Coordinates registration of specialized infrastructure concerns and domain-specific HTTP clients.
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
            .AddHealthChecksInternal(isDeployedEnvironment, configuration);

        services
            .AddAuthenticationInternal(configuration, isDeployedEnvironment)
            .AddAuthorizationInternal();

        services.AddHttpContextAccessor();

        services
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddCache(configuration);

        services
            .AddSignalRInfrastructure(configuration)
            .AddKafkaMessaging(configuration);

        services.AddWeatherHttpClients(configuration);
        services.AddBackgroundJobs();

        return services;
    }
}
