using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence.Database;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Basket.Infrastructure.Common;

/// <summary>
/// Health-check surface — Self, <see cref="BasketDbContext"/> (the SQL outbox/inbox
/// side-car), <c>redis-basket</c> (the aggregate primary store, per ADR-0016), and
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on
/// every idempotent write and fail-closed when down). Both Redis instances are isolated
/// per ADR-0016 and share one <see cref="HealthChecksOptions.RedisTimeout"/>.
/// Per-probe timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout
/// parameter, so the DB readiness probe runs under EF's command-timeout default
/// (mirrors Catalog's decision — operators who need a tighter DB-level timeout
/// switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c> into <c>EfCoreOptions</c>).
/// Two dependencies are deliberately NOT readiness probes: (1) the Kafka broker — Basket
/// has no in-process Kafka client (publish is 100% through the transactional outbox +
/// <c>outbox-relay-basket</c>; <c>OutboxWriter</c> only writes to the DB), so a broker
/// outage does not break any Basket HTTP path — checkout still commits to the outbox —
/// and broker health is owned by the relay's own readiness probe; (2) the Schema Registry —
/// the Avro serializer contacts it only cold-cache (schema-IDs are cached after first use),
/// so steady-state HTTP writes survive an SR outage. Both are boot-ordering dependencies
/// (compose <c>depends_on</c>), like Keycloak, not readiness gates.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddBasketHealthChecks(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var redisBasketConnectionString =
            configuration.GetConnectionString("Redis:Basket")
            ?? throw new InvalidOperationException(
                "Connection string 'Redis:Basket' is not configured. " +
                "Required by the Basket health-checks slice (redis-basket per ADR-0016).");

        var redisCacheConnectionString =
            configuration.GetConnectionString(IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName}' " +
                $"is not configured. Required by the Basket health-checks slice " +
                $"(redis-cache backs the idempotency-key output cache per ADR-0013 + ADR-0016).");

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddDbContextCheck<BasketDbContext>(
                name: "Basket DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                redisBasketConnectionString,
                name: "redis-basket",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout)
            .AddRedis(
                redisCacheConnectionString,
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout);

        return services;
    }
}
