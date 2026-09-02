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
    /// Margin by which the probe producer outlives the check that sent it, so the check's own
    /// cancellation — not librdkafka's timer — is what ends the await.
    /// </summary>
    private static readonly TimeSpan ProbeDeliveryGrace = TimeSpan.FromSeconds(1);

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
            // Cancelling the check abandons the await; it does NOT retract the message — Confluent.Kafka
            // registers the token to TrySetCanceled the delivery handler, IProducer exposes no purge, so
            // an undeliverable probe retires on librdkafka's own message.timeout.ms, on a producer
            // KafkaHealthCheck caches for process lifetime.
            // INVARIANT: message.timeout.ms > KafkaTimeout, or the producer gives up first and
            // KafkaTimeout stops meaning what it says about when the probe reports Unhealthy. The 1s
            // grace clears the check's own cancellation latency; retries stay off so a failed probe is
            // not left queued on that long-lived producer.
            .AddKafka(
                new ProducerConfig
                {
                    BootstrapServers = sagaKafkaOptions.BrokersFlat,
                    MessageTimeoutMs =
                        (int)(timeouts.KafkaTimeout + ProbeDeliveryGrace).TotalMilliseconds,
                    MessageSendMaxRetries = 0,
                },
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
