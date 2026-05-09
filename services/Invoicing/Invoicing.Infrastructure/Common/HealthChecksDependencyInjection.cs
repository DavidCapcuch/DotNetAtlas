using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Invoicing.Infrastructure.Messaging.Kafka.Config;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// M8 health-check surface — self, DbContext, and Kafka. Mirrors Ordering precedent.
/// Required by <c>Platform.ServiceDefaults.WebApplicationExtensions.MapPlatformHealthCheckEndpoints</c>
/// which calls <c>UseHealthChecks(...)</c> against the registered set.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddInvoicingHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerConfig = new ProducerConfig { BootstrapServers = kafkaOptions.BrokersFlat };

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<InvoicingDbContext>(
                name: "Invoicing DB",
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
