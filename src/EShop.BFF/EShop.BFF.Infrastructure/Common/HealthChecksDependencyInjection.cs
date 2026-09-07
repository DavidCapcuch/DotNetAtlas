using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Common.Config;
using EShop.BFF.Infrastructure.Messaging.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.Kafka.TopicGuard;
using Platform.ServiceDefaults.Config;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// Readiness-probe surface — <c>ApplicationLifecycle</c>, <c>redis-cache</c> and the Kafka topics.
/// The BFF holds no state of its own, so nothing here is restart-fixable and its liveness set is
/// empty (see <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>).
/// <c>redis-cache</c> reports <see cref="HealthStatus.Degraded"/> because FusionCache falls back to
/// the upstreams when it is down, so the BFF still serves. Degraded leaves readiness at 200 —
/// <c>MapPlatformHealthCheckEndpoints</c> takes the framework default rather than setting
/// <c>ResultStatusCodes</c> — so the instance stays in rotation. Why that is the right call, and
/// not the <see cref="HealthStatus.Unhealthy"/> its siblings register: ADR-0016.
/// <para>
/// Consequently no check here can fail once the host is up: ApplicationLifecycle reports only
/// while starting or stopping, the Kafka-topics check latches Healthy once its topics verify, and
/// <c>redis-cache</c> is Degraded. A 200 on readiness means this instance can serve — not that
/// every dependency is up; the per-check Prometheus gauge is what carries that.
/// </para>
/// </summary>
internal static class HealthChecksDependencyInjection
{
    public static IServiceCollection AddBffHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var redisCacheConnectionString =
            configuration.GetConnectionString(BffCacheConstants.RedisCacheConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{BffCacheConstants.RedisCacheConnectionStringName}' is not configured. " +
                $"Required by the BFF health-checks slice (redis-cache per ADR-0016).");

        // Redis needs both bounds, because the check has two paths. The registered timeout: covers
        // the connect — the token does reach ConnectAsync, and the check drops its cached multiplexer
        // on any failure, so an outage keeps reconnecting (15.1s unbounded, 1.0s with it). The client
        // timeouts below cover the steady-state ping, which takes no token at all; connectRetry=0
        // matters most, the default of 3 reconnect attempts being most of the delay. The distinct
        // connection string also gives the probe its own multiplexer, so the BFF's FusionCache client
        // keeps the reconnect behaviour its fail-safe path depends on — at the cost that this probe
        // no longer exercises that client, so a wedged FusionCache multiplexer still reports Healthy.
        // Appending is safe: on a duplicate key ConfigurationOptions.Parse takes the last occurrence.
        var redisProbeMs = (int)timeouts.RedisTimeout.TotalMilliseconds;

        var kafkaOptions = configuration
            .GetRequiredSection(BffKafkaOptions.Section)
            .Get<BffKafkaOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(BffTopicsOptions.Section)
            .Get<BffTopicsOptions>()!;

        services
            .AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddRedis(
                $"{redisCacheConnectionString},connectRetry=0,connectTimeout={redisProbeMs}" +
                $",syncTimeout={redisProbeMs},asyncTimeout={redisProbeMs}",
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Degraded,
                timeout: timeouts.RedisTimeout)
            // The BFF still probes no upstream BC and no Kafka broker health — invalidation is
            // off the request path, so a stalled consumer means stale cache, not an inability to serve.
            .AddKafkaTopicsExistenceHealthCheck(
                kafkaOptions.BrokersFlat,
                topicsOptions.GetAllTopics(),
                name: "Kafka topics",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);

        return services;
    }
}
