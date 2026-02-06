using Confluent.Kafka;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Constants;
using DotNetAtlas.Sagas.Common.Observability.HealthChecks;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.ServiceDefaults.Config;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DotNetAtlas.Sagas.Common;

/// <summary>
/// Extension methods for configuring health check endpoints.
/// </summary>
public static class HealthChecksDependencyInjection
{
    public static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<SagaHealthCheckOptions>()
            .BindConfiguration(SagaHealthCheckOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HealthCheckTimeoutsOptions>()
            .BindConfiguration(HealthCheckTimeoutsOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetSection(HealthCheckTimeoutsOptions.Section)
            .Get<HealthCheckTimeoutsOptions>() ?? new HealthCheckTimeoutsOptions();

        var sagaKafkaOptions = configuration
            .GetRequiredSection(SagaKafkaOptions.Section)
            .Get<SagaKafkaOptions>()!;

        var healthCheckKafkaProducerConfig = new ProducerConfig
        {
            BootstrapServers = sagaKafkaOptions.BrokersFlat,
            ClientId = "saga-healthcheck"
        };

        services.AddHealthChecks()
            .AddApplicationStatus(
                name: "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<SagaDbContext>(
                name: "Saga Database",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.DatabaseTag])
            .AddCheck<SagaStateMachineHealthCheck>(
                name: "Saga StateMachine",
                failureStatus: HealthStatus.Degraded,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddKafka(
                healthCheckKafkaProducerConfig,
                topic: "healthchecks",
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.MessagingTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
