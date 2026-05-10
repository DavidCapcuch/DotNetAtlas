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
/// M7 readiness-probe surface — Self, <see cref="CatalogDbContext"/> (Postgres write
/// store + atomic projection per ADR-0001 + ADR-0016), the Kafka cluster (outbox relay
/// publishes + the inbound <c>StockLevelChanged</c> consumer), <c>redis-cache</c>
/// (idempotency-key output cache per ADR-0013 + ADR-0016), and the Confluent Schema
/// Registry (Avro publish path per ADR-0007). Mirrors the Basket precedent at
/// <c>services/Basket/Basket.Infrastructure/Common/HealthChecksDependencyInjection.cs</c>;
/// the Schema-Registry <c>AddUrlGroup</c> probe is the Catalog-specific addition because
/// Catalog publishes Avro events (Basket does not).
/// </summary>
internal static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Confluent Schema Registry health probe. <c>GET /subjects</c> is the
    /// documented REST endpoint — returns 200 + a JSON array even when no subjects
    /// are registered. We do not validate the body; reachability + 200 is enough.
    /// Confluent's <c>schema-registry</c> service in <c>docker-compose.yaml</c> uses
    /// the same path for its container-level <c>healthcheck</c>, so any drift in SR
    /// versions is caught uniformly.
    /// </summary>
    private const string SchemaRegistryHealthPath = "/subjects";

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

        var producerConfig = new ProducerConfig { BootstrapServers = kafkaOptions.BrokersFlat };

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Catalog health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        var schemaRegistryUri = new Uri(
            new Uri(kafkaOptions.SchemaRegistry.Url),
            SchemaRegistryHealthPath);

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
                timeout: timeouts.KafkaTimeout)
            .AddUrlGroup(
                schemaRegistryUri,
                name: "schema-registry",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy);

        return services;
    }
}
