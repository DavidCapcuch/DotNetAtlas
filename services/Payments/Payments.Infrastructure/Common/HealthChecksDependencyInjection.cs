using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payments.Infrastructure.Messaging.Kafka.Config;
using Payments.Infrastructure.Persistence.Database;
using Platform.ServiceDefaults.Config;

namespace Payments.Infrastructure.Common;

/// <summary>
/// Minimum M5 health-check surface — self, DbContext, and Kafka. Extended surface
/// (URL probes, oauth authority, etc.) lands in M6 alongside the admin HTTP endpoints.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddPaymentsHealthChecks(
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
            .AddDbContextCheck<PaymentsDbContext>(
                name: "Payments DB",
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
