using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ordering.Infrastructure.Common.Config;
using Ordering.Infrastructure.Messaging.Kafka.Config;
using Ordering.Infrastructure.Persistence.Database;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Ordering service — Self, <see cref="OrderingDbContext"/>,
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on every
/// idempotent write and fail-closed when down), and Kafka (the in-process saga-command
/// consumer). Per-probe timeouts come from <see cref="HealthChecksOptions"/>;
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
    internal static IServiceCollection AddOrderingHealthChecks(
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
                $"is not configured. Required by the Ordering health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<OrderingDbContext>(
                name: "Ordering DB",
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
