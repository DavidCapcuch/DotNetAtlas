using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Invoicing.Infrastructure.Common.Config;
using Invoicing.Infrastructure.Messaging.Kafka.Config;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Invoicing service — Self, <see cref="InvoicingDbContext"/>,
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on every
/// idempotent write and fail-closed when down), and Kafka (the in-process enrichment-projection
/// consumers). Per-probe timeouts come from <see cref="HealthChecksOptions"/>;
/// <c>AddDbContextCheck</c> does not expose a direct timeout parameter, so the DB readiness
/// probe runs under EF's command-timeout default (operators who need a tighter DB-level
/// timeout switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c> into
/// <c>EfCoreOptions</c>). Required by
/// <c>Platform.ServiceDefaults.WebApplicationExtensions.MapPlatformHealthCheckEndpoints</c>
/// which calls <c>UseHealthChecks(...)</c> against the registered set.
/// Two dependencies are deliberately NOT readiness probes: (1) the Schema Registry — the Avro
/// serializer/deserializer contact it only cold-cache (schema-IDs are cached after first use),
/// so steady-state HTTP writes survive an SR outage; (2) Azure Blob storage — blob writes happen
/// only on the consumer-path (IssueInvoice / IssueCreditNote projections, with RetryForever +
/// DLT), while every HTTP GET mints its SAS URL client-side and survives a blob outage. Readiness
/// governs HTTP routing and cannot influence a Kafka consumer, so gating on either would 503 a
/// pod whose HTTP surface is still healthy. Both are boot-ordering dependencies (compose
/// <c>depends_on</c>); their runtime health is observed via consumer lag / DLT depth + OTEL.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddInvoicingHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
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

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Invoicing health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<InvoicingDbContext>(
                name: "Invoicing DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                redisCacheConnectionString,
                name: "redis-cache",
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
