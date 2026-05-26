using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Composition root for the Ordering Infrastructure layer. Called from
/// <c>Ordering.Api.Program.cs</c> after <c>AddServiceDefaults</c> and
/// <c>AddApplication</c>.
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
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddKafkaMessaging(configuration)
            .AddOrderingHealthChecks(configuration);

        return services;
    }
}
