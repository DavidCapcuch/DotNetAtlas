using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.Observability.HealthChecks;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;
using Platform.ServiceDefaults.Config;

namespace Platform.OutboxRelay.WorkerService.Common;

/// <summary>
/// Health-check surface for the OutboxRelay worker — ApplicationLifecycle,
/// <see cref="OutboxDbContext"/>, Kafka, and the worker-specific
/// <see cref="OutboxRelayHealthCheck"/> execution liveness probe. Per-probe
/// timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout
/// parameter, so the DB readiness probe runs under EF's command-timeout default
/// (operators who need a tighter DB-level timeout switch to <c>AddNpgSql</c> or wire
/// <c>CommandTimeout</c> into the EF Core options).
/// </summary>
internal static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Margin by which the probe producer outlives the check that sent it, so the check's own
    /// cancellation — not librdkafka's timer — is what ends the await.
    /// </summary>
    private static readonly TimeSpan ProbeDeliveryGrace = TimeSpan.FromSeconds(1);

    internal static IServiceCollection AddOutboxRelayHealthChecks(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<OutboxRelayHealthCheckOptions>()
            .BindConfiguration(OutboxRelayHealthCheckOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var kafkaProducerOptions = configuration
            .GetRequiredSection(KafkaProducerOptions.Section)
            .Get<KafkaProducerOptions>()!;

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<OutboxDbContext>(
                name: "Outbox DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            // Cancelling the check abandons the await; it does NOT retract the message — Confluent.Kafka
            // registers the token to TrySetCanceled the delivery handler, IProducer exposes no purge, so
            // an undeliverable probe retires on librdkafka's own message.timeout.ms, on a producer
            // KafkaHealthCheck caches for process lifetime.
            // INVARIANT: message.timeout.ms > KafkaTimeout, or the producer gives up first and
            // KafkaTimeout stops meaning what it says about when the probe reports Unhealthy. The 1s
            // grace clears the check's own cancellation latency; retries stay off so a failed probe is
            // not left queued on that long-lived producer.
            // Never build this from kafkaProducerOptions — it is itself a ProducerConfig, so it is
            // directly assignable here, and its publish-path MessageTimeoutMs (5 min) reinstates
            // exactly the wedge this cap exists to avoid.
            .AddKafka(
                new ProducerConfig
                {
                    BootstrapServers = kafkaProducerOptions.BootstrapServers,
                    MessageTimeoutMs =
                        (int)(timeouts.KafkaTimeout + ProbeDeliveryGrace).TotalMilliseconds,
                    MessageSendMaxRetries = 0,
                },
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout)
            // The only liveness-tagged check in this solution, and a deliberate exception to the
            // rule on ServiceDefaultHealthCheckTags.LivenessTag — carried with a known sharp edge.
            // The probe reports Unhealthy purely on publish-loop staleness, and the loop only
            // succeeds when both Postgres and Kafka are reachable, so a dependency outage lasting
            // longer than UnhealthyThreshold fails liveness on every relay replica at once — the
            // cascading restart the rule exists to prevent. It cannot currently distinguish a
            // wedged loop (a restart helps) from a dependency outage (a restart hurts). Making it
            // dependency-aware, or moving it off liveness, is open work rather than settled design.
            .AddCheck<OutboxRelayHealthCheck>(
                name: "OutboxRelay Execution",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.OutboxRelayExecutionTimeout);
        services.AddSingleton<OutboxRelayHealthCheck>();

        return services;
    }
}
