using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Common.Constants;
using Notifications.Infrastructure.Common.Persistence.Database;
using Platform.ServiceDefaults.Config;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for health checks infrastructure.
/// </summary>
public static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Configures health checks for the application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeoutsOptions = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BrokersFlat
        };

        services.AddHealthChecks()
            .AddApplicationStatus("Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeoutsOptions.SelfTimeout)
            .AddDbContextCheck<NotificationDbContext>(
                name: "Payment DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddKafka(producerConfig, "healthchecks", "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.MessagingTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.KafkaTimeout);

        return services;
    }
}
