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

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Ordering service — Self, <see cref="OrderingDbContext"/>,
/// and Kafka. Per-probe timeouts come from <see cref="HealthChecksOptions"/>;
/// <c>AddDbContextCheck</c> does not expose a direct timeout parameter, so the DB readiness
/// probe runs under EF's command-timeout default (Catalog M10 precedent — operators who need
/// a tighter DB-level timeout switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c>
/// into <c>EfCoreOptions</c>).
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

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<OrderingDbContext>(
                name: "Ordering DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddKafka(
                producerConfig,
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }
}
