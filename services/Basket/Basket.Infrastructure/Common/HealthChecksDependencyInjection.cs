using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Idempotency;

namespace Basket.Infrastructure.Common;

/// <summary>
/// Health-check surface — ApplicationLifecycle, <see cref="BasketDbContext"/> (the SQL outbox/inbox
/// side-car), <c>redis-basket</c> (the aggregate primary store, per ADR-0016), and
/// <c>redis-cache</c> (the idempotency-key OutputCache per ADR-0013 + ADR-0016, hit on
/// every idempotent write and fail-closed when down). Both Redis instances are isolated
/// per ADR-0016 and share one <see cref="HealthChecksOptions.RedisTimeout"/>.
/// Per-probe timeouts come from <see cref="HealthChecksOptions"/>.
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

        // Bounds both phases of the database probe below. Worst case is twice this value: the
        // connect and the query each get it.
        var dbProbeSeconds = (int)timeouts.DbTimeout.TotalSeconds;

        // Redis needs both bounds, because the check has two paths. The registered timeout: covers
        // the connect — the token does reach ConnectAsync, and the check drops its cached multiplexer
        // on any failure, so an outage keeps reconnecting (15.1s unbounded, 1.0s with it). The client
        // timeouts below cover the steady-state ping, which takes no token at all; connectRetry=0
        // matters most, the default of 3 reconnect attempts being most of the delay. The distinct
        // connection string also gives the probe its own multiplexer, leaving the application client
        // its own retry behaviour; appending is safe, since ConfigurationOptions.Parse takes the last
        // occurrence of a duplicate key.
        var redisProbeMs = (int)timeouts.RedisTimeout.TotalMilliseconds;

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
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            // A deadline cannot bound this check: the retrying execution strategy starts a fresh attempt
            // inside one, and against a pooled connection the hang moves from the connect to the query,
            // where a connect timeout does not apply. The probe therefore opens its own unpooled
            // connection, whose Timeout and CommandTimeout bound both phases and touch nothing the
            // application uses. Pooling stays off because a stale pooled connection is tried, fails, and
            // then a fresh one is opened — paying the timeout twice (measured 6.0s against a paused
            // server, versus 2.0s unpooled, either side of the orchestrator budget). The cost of that
            // isolation: the probe no longer touches the pool the application uses, so pool exhaustion
            // reports Healthy here — watch the client connection metrics for it, not readiness.
            .AddDbContextCheck<BasketDbContext>(
                name: "Basket DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                customTestQuery: async (context, cancellationToken) =>
                {
                    var probeConnectionString = new NpgsqlConnectionStringBuilder(
                        context.Database.GetConnectionString())
                    {
                        Timeout = dbProbeSeconds,
                        CommandTimeout = dbProbeSeconds,
                        Pooling = false,
                    }.ConnectionString;

                    await using var connection = new NpgsqlConnection(probeConnectionString);
                    await connection.OpenAsync(cancellationToken);

                    await using var command = new NpgsqlCommand("SELECT 1", connection);
                    await command.ExecuteScalarAsync(cancellationToken);
                    return true;
                })
            .AddRedis(
                $"{redisBasketConnectionString},connectRetry=0,connectTimeout={redisProbeMs}" +
                $",syncTimeout={redisProbeMs},asyncTimeout={redisProbeMs}",
                name: "redis-basket",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout)
            .AddRedis(
                $"{redisCacheConnectionString},connectRetry=0,connectTimeout={redisProbeMs}" +
                $",syncTimeout={redisProbeMs},asyncTimeout={redisProbeMs}",
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout);

        return services;
    }
}
