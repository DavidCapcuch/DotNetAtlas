using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Observability.HealthChecks;
using SagaOrchestrators.Common.Persistence.Database;

namespace SagaOrchestrators.Common;

/// <summary>
/// Health-check surface for the Saga orchestrator — ApplicationLifecycle,
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
    /// <summary>
    /// Fraction of the Kafka check window the probe producer gets as its delivery cap; the remaining
    /// 20% is the margin in which an undeliverable probe is purged before the check window closes.
    /// Share the factor, never the resulting millisecond value — windows differ across services, so a
    /// literal is only correct for the one window it was computed against.
    /// </summary>
    private const double ProbeHeadroomFactor = 0.8;

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

        // Fail fast and self-heal: retries off, and librdkafka's 5-min default message.timeout.ms would
        // leave an undeliverable probe (broker blip) queued long after the check that sent it gave up,
        // starving later probes on this process-lifetime producer and wedging readiness Unhealthy until
        // a restart. The AddKafka timeout below cannot prevent that — it abandons the awaiting task but
        // cannot retract a message already handed to librdkafka.
        // INVARIANT: message.timeout.ms and socket.timeout.ms stay strictly below that timeout, so an
        // abandoned probe is purged before the next one runs. Validation has not run at this point, so
        // what holds that floor is [Range] on HealthChecksOptions.KafkaTimeout failing startup before a
        // probe ever executes — a derived 0 would mean *infinite* to librdkafka.
        var producerTimeoutMs = (int)(timeouts.KafkaTimeout.TotalMilliseconds * ProbeHeadroomFactor);

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = sagaKafkaOptions.BrokersFlat,
            MessageTimeoutMs = producerTimeoutMs,
            SocketTimeoutMs = producerTimeoutMs,
            MessageSendMaxRetries = 0,
        };

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
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
