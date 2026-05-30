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
/// and Kafka (the in-process payment-commands consumer). Per-probe timeouts come from
/// <see cref="HealthChecksOptions"/>; <c>AddDbContextCheck</c> does not expose a direct
/// timeout parameter, so the DB readiness probe runs under EF's command-timeout default
/// (operators who need a tighter DB-level timeout switch to <c>AddNpgSql</c> or wire
/// <c>CommandTimeout</c> into <c>EfCoreOptions</c>). No Redis check — Payments has no
/// idempotency cache layer. The Schema Registry is deliberately NOT a readiness probe: the
/// Avro serializer/deserializer contact it only cold-cache (schema-IDs are cached after
/// first use), so steady-state operation survives an SR outage — SR is a boot-ordering
/// dependency (compose <c>depends_on</c>), like Keycloak, not a readiness gate.
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

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<PaymentsDbContext>(
                name: "Payments DB",
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
