using EShop.BFF.Infrastructure.Caching;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Config;
using StackExchange.Redis;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// BFF health checks: <c>self</c> (liveness) and <c>redis-cache</c> (readiness). Surfaced by
/// <c>MapPlatformHealthCheckEndpoints</c> at <c>/api/healthz</c> + <c>/api/readiness</c>. Request-time
/// graceful degradation when redis-cache is down lives in FusionCache (it falls back to the upstreams);
/// the readiness gate simply reflects the declared dependency.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    public static IServiceCollection AddBffHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddApplicationStatus(
                "self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag])
            .AddRedis(
                connectionMultiplexerFactory: serviceProvider =>
                    serviceProvider.GetRequiredKeyedService<IConnectionMultiplexer>(BffCacheConstants.CacheName),
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);

        return services;
    }
}
