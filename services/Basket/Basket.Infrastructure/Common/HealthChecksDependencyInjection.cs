using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Messaging.Kafka.Config;
using Basket.Infrastructure.Persistence.Database;
using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;

namespace Basket.Infrastructure.Common;

/// <summary>
/// Health-check surface — self, <see cref="BasketDbContext"/>
/// (the SQL outbox/inbox side-car), <c>redis-basket</c> (the aggregate primary
/// store, per ADR-0016), and Kafka. The redis-cache instance used by
/// FastEndpoints idempotency is wired at the host level alongside the
/// idempotency middleware itself; only <c>redis-basket</c> is registered here.
/// Per-probe timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout
/// parameter, so the DB readiness probe runs under EF's command-timeout
/// default (mirrors Catalog's M10 decision — operators who need a tighter
/// DB-level timeout switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c>
/// into <c>EfCoreOptions</c>).
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddBasketHealthChecks(
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

        var redisBasketConnectionString =
            configuration.GetConnectionString("Redis:Basket")
            ?? throw new InvalidOperationException(
                "Connection string 'Redis:Basket' is not configured. " +
                "Required by the Basket health-checks slice (redis-basket per ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<BasketDbContext>(
                name: "Basket DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                redisBasketConnectionString,
                name: "redis-basket",
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
