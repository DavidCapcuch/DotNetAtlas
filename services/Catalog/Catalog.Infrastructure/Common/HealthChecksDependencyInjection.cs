using Catalog.Infrastructure.Common.Config;
using Catalog.Infrastructure.Messaging.Kafka.Config;
using Catalog.Infrastructure.Persistence.Database;
using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Catalog.Infrastructure.Common;

/// <summary>
/// Readiness-probe surface — Self, <see cref="CatalogDbContext"/> (Postgres write
/// store + atomic projection per ADR-0001 + ADR-0016), <c>redis-cache</c> (the
/// idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on every idempotent write
/// and fail-closed when down), and the Kafka cluster (outbox relay publishes + the
/// in-process inbound <c>StockLevelChangedEvent</c> consumer). The Schema Registry is
/// deliberately NOT a readiness probe: the Avro serializer/deserializer contact it only
/// cold-cache (schema-IDs are cached after first use on both the produce and consume
/// paths), so steady-state operation survives an SR outage — SR is a boot-ordering
/// dependency (compose <c>depends_on</c>), like Keycloak, not a readiness gate.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddCatalogHealthChecks(
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

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BrokersFlat,
            // Health-probe producer must fail fast and self-heal: cap message + socket timeouts so an
            // undeliverable probe (transient broker outage) is purged within the check window instead of
            // queuing in librdkafka for the 5-min default message.timeout.ms — which otherwise starves
            // later probes and wedges this readiness check Unhealthy until a process restart.
            MessageTimeoutMs = 4000,
            SocketTimeoutMs = 4000,
            MessageSendMaxRetries = 0,
        };

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Catalog health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<CatalogDbContext>(
                name: "Catalog DB",
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
