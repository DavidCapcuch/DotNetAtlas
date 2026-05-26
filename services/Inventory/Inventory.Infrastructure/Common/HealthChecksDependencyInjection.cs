using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Inventory.Infrastructure.Common.Config;
using Inventory.Infrastructure.Messaging.Kafka.Config;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Inventory service — Self, <see cref="InventoryDbContext"/>,
/// and Kafka. Per-probe timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout parameter,
/// so <see cref="HealthChecksOptions.DatabaseTimeout"/> is enforced via a custom test query
/// that cancels its own <see cref="CancellationTokenSource"/>. No Redis check — the
/// redis-cache idempotency store is shared infra, monitored at the platform level.
/// </summary>
internal static class HealthChecksDependencyInjection
{
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

        var producerConfig = new ProducerConfig { BootstrapServers = kafkaOptions.BrokersFlat };

        var databaseTimeout = timeouts.DatabaseTimeout;

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<InventoryDbContext>(
                name: "Inventory DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                customTestQuery: async (db, ct) =>
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(databaseTimeout);
                    return await db.Database.CanConnectAsync(cts.Token).ConfigureAwait(false);
                })
            .AddKafka(
                producerConfig,
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
