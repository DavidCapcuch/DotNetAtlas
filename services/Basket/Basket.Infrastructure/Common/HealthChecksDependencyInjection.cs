using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Messaging.Kafka.Config;
using Basket.Infrastructure.Persistence.Database;
using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
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
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddBasketHealthChecks(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerConfig = new ProducerConfig { BootstrapServers = kafkaOptions.BrokersFlat };

        var redisBasketConnectionString =
            configuration.GetConnectionString(ConnectionStringNames.BasketRedis)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringNames.BasketRedis}' is not configured. " +
                $"Required by the Basket health-checks slice (redis-basket per ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<BasketDbContext>(
                name: "Basket DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                redisBasketConnectionString,
                name: "redis-basket",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddKafka(
                producerConfig,
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy);

        return services;
    }
}
