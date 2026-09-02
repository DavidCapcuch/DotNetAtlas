using Confluent.Kafka;
using Inventory.Infrastructure.Common.Config;
using Inventory.Infrastructure.Messaging.Kafka.Config;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Inventory service — ApplicationLifecycle, <see cref="InventoryDbContext"/>,
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on every
/// idempotent write and fail-closed when down), and Kafka (the in-process reservation /
/// stock-init consumers). Per-probe timeouts come from <see cref="HealthChecksOptions"/>;
/// <c>AddDbContextCheck</c> does not expose a direct timeout parameter, so the DB readiness
/// probe runs under EF's command-timeout default (operators who need a tighter DB-level
/// timeout switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c> into
/// <c>EfCoreOptions</c>). The Schema Registry is deliberately NOT a readiness probe: the Avro
/// serializer/deserializer contact it only cold-cache (schema-IDs are cached after first use),
/// so steady-state HTTP writes survive an SR outage — SR is a boot-ordering dependency
/// (compose <c>depends_on</c>), like Keycloak, not a readiness gate.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Margin by which the probe producer outlives the check that sent it, so the check's own
    /// cancellation — not librdkafka's timer — is what ends the await.
    /// </summary>
    private static readonly TimeSpan ProbeDeliveryGrace = TimeSpan.FromSeconds(1);

    internal static IServiceCollection AddInventoryHealthChecks(
        this IServiceCollection services,
        ConfigurationManager configuration)
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

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Inventory health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<InventoryDbContext>(
                name: "Inventory DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                redisCacheConnectionString,
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
                },
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
