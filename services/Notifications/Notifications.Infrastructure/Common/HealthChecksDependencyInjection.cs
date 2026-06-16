using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Persistence.Database;
using Platform.ServiceDefaults.Config;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Notifications worker — Self,
/// <see cref="NotificationsDbContext"/>, and Kafka. Per-probe timeouts come from
/// <see cref="HealthChecksOptions"/>; the <c>AddDbContextCheck</c> EF Core extension
/// does not expose a direct timeout parameter, so the DB readiness probe runs under
/// EF's command-timeout default (operators who need a tighter DB-level timeout switch
/// to <c>AddNpgSql</c> or wire <c>CommandTimeout</c> into <c>EfCoreOptions</c>). No
/// Redis check — Notifications has no idempotency cache layer. The Schema Registry is
/// deliberately NOT a readiness probe: the in-process EmailCommands consumer's Avro
/// deserializer contacts it only cold-cache (schema-IDs are cached after first use), so
/// steady-state consumption survives an SR outage — SR is a boot-ordering dependency
/// (compose <c>depends_on</c>), like Keycloak, not a readiness gate.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddNotificationsHealthChecks(
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

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BrokersFlat,
            // Health-probe producer must fail fast and self-heal: cap message + socket timeouts so an
            // undeliverable probe (transient broker outage) is purged within the check window instead of
            // queuing in librdkafka for the 5-min default message.timeout.ms — which otherwise starves
            // later probes and wedges this readiness check Unhealthy until a process restart.
            MessageTimeoutMs = 4000,
            SocketTimeoutMs = 4000,
            MessageSendMaxRetries = 0,
        };

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<NotificationsDbContext>(
                name: "Notifications DB",
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
