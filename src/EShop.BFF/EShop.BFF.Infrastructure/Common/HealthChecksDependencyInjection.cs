using EShop.BFF.Infrastructure.Caching;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Config;
using StackExchange.Redis;

namespace EShop.BFF.Infrastructure.Common;

/// <summary>
/// Readiness-probe surface — <c>self</c> and <c>redis-cache</c>. The BFF holds no state of its own,
/// so nothing here is restart-fixable and its liveness set is empty
/// (see <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/>). Request-time graceful degradation
/// when redis-cache is down lives in FusionCache (it falls back to the upstreams); the readiness
/// gate simply reflects the declared dependency.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    public static IServiceCollection AddBffHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddApplicationStatus(
                "self",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag])
            .AddRedis(
                connectionMultiplexerFactory: serviceProvider =>
                    serviceProvider.GetRequiredKeyedService<IConnectionMultiplexer>(BffCacheConstants.CacheName),
                name: "redis-cache",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);

        return services;
    }
}
