using Confluent.Kafka;
using Invoicing.Infrastructure.Common.Config;
using Invoicing.Infrastructure.Messaging.Kafka.Config;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Invoicing service — ApplicationLifecycle, <see cref="InvoicingDbContext"/>,
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on every
/// idempotent write and fail-closed when down), and Kafka (the in-process enrichment-projection
/// consumers). Per-probe timeouts come from <see cref="HealthChecksOptions"/>;
/// <c>AddDbContextCheck</c> does not expose a direct timeout parameter, so the DB readiness
/// probe runs under EF's command-timeout default (operators who need a tighter DB-level
/// timeout switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c> into
/// <c>EfCoreOptions</c>). Required by
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
    /// Fraction of the Kafka check window the probe producer gets as its delivery cap; the remaining
    /// 20% is the margin in which an undeliverable probe is purged before the check window closes.
    /// Share the factor, never the resulting millisecond value — windows differ across services, so a
    /// literal is only correct for the one window it was computed against.
    /// </summary>
    private const double ProbeHeadroomFactor = 0.8;

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

        var kafkaOptions = configuration
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
            BootstrapServers = kafkaOptions.BrokersFlat,
            MessageTimeoutMs = producerTimeoutMs,
            SocketTimeoutMs = producerTimeoutMs,
            MessageSendMaxRetries = 0,
        };

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Invoicing health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<InvoicingDbContext>(
                name: "Invoicing DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                redisCacheConnectionString,
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout)
            .AddKafka(
                producerConfig,
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
