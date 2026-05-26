using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payments.Infrastructure.Common.Config;
using Payments.Infrastructure.Messaging.Kafka.Config;
using Payments.Infrastructure.Persistence.Database;
using Platform.ServiceDefaults.Config;

namespace Payments.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Payments service — Self, <see cref="PaymentsDbContext"/>,
/// and Kafka. Per-probe timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout parameter,
/// so <see cref="HealthChecksOptions.DatabaseTimeout"/> is enforced via a custom test query
/// that cancels its own <see cref="CancellationTokenSource"/>.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddPaymentsHealthChecks(
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
            .AddDbContextCheck<PaymentsDbContext>(
                name: "Payments DB",
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
