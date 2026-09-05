using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
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
/// timeouts come from <see cref="HealthChecksOptions"/>.
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

        // Bounds both phases of the database probe below. Worst case is twice this value: the
        // connect and the query each get it.
        var dbProbeSeconds = (int)timeouts.DbTimeout.TotalSeconds;

        var kafkaProducerOptions = configuration
            .GetRequiredSection(KafkaProducerOptions.Section)
            .Get<KafkaProducerOptions>()!;

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            // A deadline cannot bound this check: the retrying execution strategy starts a fresh attempt
            // inside one, and against a pooled connection the hang moves from the connect to the query,
            // where a connect timeout does not apply. The probe therefore opens its own unpooled
            // connection, whose Timeout and CommandTimeout bound both phases and touch nothing the
            // application uses. Pooling stays off because a stale pooled connection is tried, fails, and
            // then a fresh one is opened — paying the timeout twice (measured 6.0s against a paused
            // server, versus 2.0s unpooled, either side of the orchestrator budget). The cost of that
            // isolation: the probe no longer touches the pool the application uses, so pool exhaustion
            // reports Healthy here — watch the client connection metrics for it, not readiness.
            .AddDbContextCheck<OutboxDbContext>(
                name: "Outbox DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                customTestQuery: async (context, cancellationToken) =>
                {
                    var probeConnectionString = new NpgsqlConnectionStringBuilder(
                        context.Database.GetConnectionString())
                    {
                        Timeout = dbProbeSeconds,
                        CommandTimeout = dbProbeSeconds,
                        Pooling = false,
                    }.ConnectionString;

                    await using var connection = new NpgsqlConnection(probeConnectionString);
                    await connection.OpenAsync(cancellationToken);

                    await using var command = new NpgsqlCommand("SELECT 1", connection);
                    await command.ExecuteScalarAsync(cancellationToken);
                    return true;
                })
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
                    AllowAutoCreateTopics = false,
                },
                topic: "healthchecks",
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
            // No timeout: every branch returns Task.FromResult, so a registration Timeout could
            // never fire — rationale on ServiceDefaultHealthCheckTags.
            .AddCheck<OutboxRelayHealthCheck>(
                name: "OutboxRelay Execution",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy);
        services.AddSingleton<OutboxRelayHealthCheck>();

        return services;
    }
}
