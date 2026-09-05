using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Platform.ServiceDefaults.Config;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Observability.HealthChecks;
using SagaOrchestrators.Common.Persistence.Database;

namespace SagaOrchestrators.Common;

/// <summary>
/// Health-check surface for the Saga orchestrator — ApplicationLifecycle,
/// <see cref="SagaDbContext"/>, Kafka, and the saga-specific
/// <see cref="StuckSagaHealthCheck"/>.
/// Per-probe timeouts come from <see cref="HealthChecksOptions"/>.
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
        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        // Bounds both phases of the database probe below. Worst case is twice this value: the
        // connect and the query each get it.
        var dbProbeSeconds = (int)timeouts.DbTimeout.TotalSeconds;

        var sagaKafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

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
            .AddDbContextCheck<SagaDbContext>(
                name: "Saga DB",
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
            // Reads the count StuckSagaMetricsCollector sweeps in the background, so it performs no
            // I/O: no timeout: to enforce, and no failureStatus:, which is consulted only when the
            // delegate throws. Its StuckSagaMetrics and StuckSagaOptions dependencies are
            // registered by AddSagaStateObservability(), not here.
            .AddCheck<StuckSagaHealthCheck>(
                name: "Stuck Sagas",
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
                    AllowAutoCreateTopics = false,
                },
                topic: "healthchecks",
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
