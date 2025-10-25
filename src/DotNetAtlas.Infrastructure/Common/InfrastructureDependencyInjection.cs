using DotNetAtlas.Infrastructure.Common.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Main orchestrator for infrastructure dependencies.
/// Coordinates registration of specialized infrastructure concerns and domain-specific HTTP clients.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isClusterEnvironment)
    {
        services
            .AddOpenTelemetry(isClusterEnvironment, configuration)
            .AddHealthChecksInternal(configuration);

        services
            .AddAuthenticationInternal(configuration, isClusterEnvironment)
            .AddAuthorizationInternal();

        services
            .AddDatabase(configuration, isClusterEnvironment)
            .AddCache(configuration);

        services
            .AddSignalRInfrastructure(configuration)
            .AddKafkaMessaging(configuration);

        services.AddWeatherHttpClients(configuration);
        services.AddBackgroundJobs();

        return services;
    }
}
