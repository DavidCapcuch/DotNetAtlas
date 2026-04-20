using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.ServiceDefaults.Idempotency;

/// <summary>
/// DI helper for FastEndpoints' <c>.Idempotency()</c> HTTP filter backed by Redis (ADR-0013).
/// </summary>
public static class IdempotencyKeyServiceCollectionExtensions
{
    /// <summary>
    /// Configuration connection-string name read for the backing Redis instance:
    /// <c>ConnectionStrings:Redis:Cache</c> (ADR-0016).
    /// </summary>
    public const string RedisConnectionStringName = "Redis:Cache";

    /// <summary>
    /// Registers the StackExchange-Redis-backed <see cref="Microsoft.AspNetCore.OutputCaching.IOutputCacheStore"/>
    /// required by FastEndpoints' native <c>.Idempotency()</c> chain method. The store is namespaced
    /// with an instance-name prefix <c>{serviceName}:idem:</c> so multiple services sharing the
    /// <c>redis-cache</c> instance do not collide.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>ConnectionStrings:Redis:Cache</c> is missing or empty. ADR-0013 + ADR-0016
    /// require a real Redis instance; a silent fallback to an in-memory store would violate the
    /// "idempotency survives service restart" guarantee.
    /// </exception>
    public static IServiceCollection AddIdempotencyKeyOutputCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var connectionString = configuration.GetConnectionString(RedisConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Idempotency-Key output cache requires connection string " +
                $"'ConnectionStrings:{RedisConnectionStringName}' (ADR-0013 + ADR-0016). " +
                $"Configure the redis-cache endpoint or do not call AddIdempotencyKeyOutputCache.");
        }

        services.AddStackExchangeRedisOutputCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = $"{serviceName}:idem:";
        });
        services.AddOutputCache();

        return services;
    }
}
