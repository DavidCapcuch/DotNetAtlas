using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.Common.Constants;
using Platform.OutboxRelay.WorkerService.Observability.HealthChecks;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;
using Platform.ServiceDefaults.Config;

namespace Platform.OutboxRelay.WorkerService.Common;

/// <summary>
/// Dependency injection extensions for health checks infrastructure.
/// Configures health checks for database, messaging, and service execution monitoring.
/// </summary>
public static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Configures health checks for the OutboxRelay worker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<OutboxRelayHealthCheckOptions>()
            .BindConfiguration(OutboxRelayHealthCheckOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeoutsOptions = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var kafkaProducerOptions = configuration
            .GetRequiredSection(KafkaProducerOptions.Section)
            .Get<KafkaProducerOptions>()!;

        services.AddHealthChecks()
            .AddApplicationStatus("Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeoutsOptions.SelfTimeout)
            .AddDbContextCheck<OutboxDbContext>(
                name: "Outbox DbContext",
                tags:
                [
                    ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.DatabaseTag
                ],
                failureStatus: HealthStatus.Unhealthy)
            .AddKafka(kafkaProducerOptions, "healthchecks", "Kafka",
                tags:
                [
                    ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.MessagingTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.KafkaTimeout)
            .AddCheck<OutboxRelayHealthCheck>(
                name: "OutboxRelay Execution",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.OutboxRelayExecutionTimeout);
        services.AddSingleton<OutboxRelayHealthCheck>();

        return services;
    }
}
