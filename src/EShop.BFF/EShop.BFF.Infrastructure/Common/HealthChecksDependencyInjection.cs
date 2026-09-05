using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Common.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// Readiness-probe surface — <c>ApplicationLifecycle</c> and <c>redis-cache</c>. The BFF holds no
/// state of its own, so nothing here is restart-fixable and its liveness set is empty
/// (see <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>). Request-time graceful degradation
/// when redis-cache is down lives in FusionCache (it falls back to the upstreams); the readiness
/// gate simply reflects the declared dependency.
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

        services
            .AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag])
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
