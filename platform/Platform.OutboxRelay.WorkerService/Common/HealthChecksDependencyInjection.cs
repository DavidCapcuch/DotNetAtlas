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
    /// Fraction of the Kafka check window the probe producer gets as its delivery cap; the remaining
    /// 20% is the margin in which an undeliverable probe is purged before the check window closes.
    /// Share the factor, never the resulting millisecond value — windows differ across services, so a
    /// literal is only correct for the one window it was computed against.
    /// </summary>
    private const double ProbeHeadroomFactor = 0.8;

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

        // Fail fast and self-heal: retries off, and librdkafka's 5-min default message.timeout.ms would
        // leave an undeliverable probe (broker blip) queued long after the check that sent it gave up,
        // starving later probes on this process-lifetime producer and wedging readiness Unhealthy until
        // a restart. The AddKafka timeout below cannot prevent that — it abandons the awaiting task but
        // cannot retract a message already handed to librdkafka.
        // INVARIANT: message.timeout.ms and socket.timeout.ms stay strictly below that timeout, so an
        // abandoned probe is purged before the next one runs. Validation has not run at this point, so
        // what holds that floor is [Range] on HealthChecksOptions.KafkaTimeout failing startup before a
        // probe ever executes — a derived 0 would mean *infinite* to librdkafka.
        // Never build this from kafkaProducerOptions — it is itself a ProducerConfig, so it is directly
        // assignable here, and its publish-path MessageTimeoutMs (5 min) reinstates exactly this wedge.
        var producerTimeoutMs = (int)(timeouts.KafkaTimeout.TotalMilliseconds * ProbeHeadroomFactor);

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaProducerOptions.BootstrapServers,
            MessageTimeoutMs = producerTimeoutMs,
            SocketTimeoutMs = producerTimeoutMs,
            MessageSendMaxRetries = 0,
        };

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<OutboxDbContext>(
                name: "Outbox DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddKafka(
                producerConfig,
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
