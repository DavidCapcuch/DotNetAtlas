using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Observability.HealthChecks;
using SagaOrchestrators.Common.Persistence.Database;

namespace SagaOrchestrators.Common;

/// <summary>
/// Health-check surface for the Saga orchestrator — Self,
/// <see cref="SagaDbContext"/>, Kafka, and the saga-specific
/// <see cref="SagaStateMachineHealthCheck"/> stuck-state probe (intentionally
/// reports <see cref="HealthStatus.Degraded"/> rather than Unhealthy so
/// orchestrator restarts are not triggered by transient stuck-saga thresholds).
/// Per-probe timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout
/// parameter, so the DB readiness probe runs under EF's command-timeout default.
/// The Schema Registry is deliberately NOT a readiness probe: the saga's Avro
/// serializer/deserializer contact it only cold-cache (schema-IDs are cached after first
/// use on both the consume and produce paths), so steady-state orchestration survives an
/// SR outage — SR is a boot-ordering dependency (compose <c>depends_on</c>), like
/// Keycloak, not a readiness gate.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddSagaHealthChecks(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<SagaHealthCheckOptions>()
            .BindConfiguration(SagaHealthCheckOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var sagaKafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerConfig = new ProducerConfig { BootstrapServers = sagaKafkaOptions.BrokersFlat };

        services.AddHealthChecks()
            .AddApplicationStatus(
                name: "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<SagaDbContext>(
                name: "Saga DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddCheck<SagaStateMachineHealthCheck>(
                name: "Saga StateMachine",
                failureStatus: HealthStatus.Degraded,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddKafka(
                producerConfig,
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
