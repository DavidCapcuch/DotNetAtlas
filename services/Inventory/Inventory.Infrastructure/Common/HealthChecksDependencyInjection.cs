using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Inventory.Infrastructure.Messaging.Kafka.Config;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Inventory service. Mirrors Basket's M6
/// shape (Self / DbContext / Kafka). No Redis check — Inventory has no
/// primary-store Redis (the redis-cache used by FastEndpoints idempotency
/// is owned by Inventory.API and shared with other BCs; per-BC health
/// monitoring of that infra is the platform team's responsibility).
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddInventoryHealthChecks(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerConfig = new ProducerConfig { BootstrapServers = kafkaOptions.BrokersFlat };

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<InventoryDbContext>(
                name: "Inventory DB",
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
