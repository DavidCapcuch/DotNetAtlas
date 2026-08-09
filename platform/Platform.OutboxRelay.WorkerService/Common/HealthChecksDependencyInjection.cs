using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.Observability.HealthChecks;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;
using Platform.ServiceDefaults.Config;

namespace Platform.OutboxRelay.WorkerService.Common;

/// <summary>
/// Health-check surface for the OutboxRelay worker — Self,
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

        var producerConfig = new ProducerConfig { BootstrapServers = kafkaProducerOptions.BootstrapServers };

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag])
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
