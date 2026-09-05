using Confluent.Kafka;
using Invoicing.Infrastructure.Common.Config;
using Invoicing.Infrastructure.Messaging.Kafka.Config;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Invoicing service — ApplicationLifecycle, <see cref="InvoicingDbContext"/>,
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on every
/// idempotent write and fail-closed when down), and Kafka (the in-process enrichment-projection
/// consumers). Per-probe timeouts come from <see cref="HealthChecksOptions"/>.
/// Required by
/// <c>Platform.ServiceDefaults.WebApplicationExtensions.MapPlatformHealthCheckEndpoints</c>
/// which calls <c>UseHealthChecks(...)</c> against the registered set.
/// Two dependencies are deliberately NOT readiness probes: (1) the Schema Registry — the Avro
/// serializer/deserializer contact it only cold-cache (schema-IDs are cached after first use),
/// so steady-state HTTP writes survive an SR outage; (2) Azure Blob storage — blob writes happen
/// only on the consumer-path (IssueInvoice / IssueCreditNote projections, with RetryForever +
/// DLT), while every HTTP GET mints its SAS URL client-side and survives a blob outage. Readiness
/// governs HTTP routing and cannot influence a Kafka consumer, so gating on either would 503 a
/// pod whose HTTP surface is still healthy. Both are boot-ordering dependencies (compose
/// <c>depends_on</c>); their runtime health is observed via consumer lag / DLT depth + OTEL.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Margin by which the probe producer outlives the check that sent it, so the check's own
    /// cancellation — not librdkafka's timer — is what ends the await.
    /// </summary>
    private static readonly TimeSpan ProbeDeliveryGrace = TimeSpan.FromSeconds(1);

    internal static IServiceCollection AddInvoicingHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
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

        // Redis needs both bounds, because the check has two paths. The registered timeout: covers
        // the connect — the token does reach ConnectAsync, and the check drops its cached multiplexer
        // on any failure, so an outage keeps reconnecting (15.1s unbounded, 1.0s with it). The client
        // timeouts below cover the steady-state ping, which takes no token at all; connectRetry=0
        // matters most, the default of 3 reconnect attempts being most of the delay. The distinct
        // connection string also gives the probe its own multiplexer, leaving the application client
        // its own retry behaviour; appending is safe, since ConfigurationOptions.Parse takes the last
        // occurrence of a duplicate key.
        var redisProbeMs = (int)timeouts.RedisTimeout.TotalMilliseconds;

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Invoicing health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            // A deadline cannot bound this check: against a pooled connection the hang moves from the
            // connect to the query, where a connect timeout does not apply. (Unlike its siblings this
            // context has no retrying execution strategy — ADR-0018 — so only that half applies here.) The probe therefore opens its own unpooled
            // connection, whose Timeout and CommandTimeout bound both phases and touch nothing the
            // application uses. Pooling stays off because a stale pooled connection is tried, fails, and
            // then a fresh one is opened — paying the timeout twice (measured 6.0s against a paused
            // server, versus 2.0s unpooled, either side of the orchestrator budget). The cost of that
            // isolation: the probe no longer touches the pool the application uses, so pool exhaustion
            // reports Healthy here — watch the client connection metrics for it, not readiness.
            .AddDbContextCheck<InvoicingDbContext>(
                name: "Invoicing DB",
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
            .AddRedis(
                $"{redisCacheConnectionString},connectRetry=0,connectTimeout={redisProbeMs}" +
                $",syncTimeout={redisProbeMs},asyncTimeout={redisProbeMs}",
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout)
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
                    BootstrapServers = kafkaOptions.BrokersFlat,
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
