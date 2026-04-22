using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Strongly-typed configuration for the Basket aggregate's <c>redis-basket</c>
/// persistence path. Bound from the <c>Basket:Redis</c> configuration section.
/// </summary>
public sealed class BasketRedisOptions
{
    public const string Section = "Basket:Redis";

    /// <summary>
    /// Sliding TTL applied to every basket key on save. Default 30 days — basket.md &#xa7; 5.3.
    /// </summary>
    [Range(1, 365)]
    public int TtlDays { get; set; } = 30;

    /// <summary>
    /// Lifetime of the per-user CAS lock (<c>basket-lock:{userId}</c>). Must be long
    /// enough for the load-check-write round-trip, short enough that a crashed
    /// writer does not keep another request blocked.
    /// </summary>
    [Range(1, 60)]
    public int LockTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Delay between lock-acquisition retries. Multiplied by
    /// <see cref="LockMaxRetries"/> yields the worst-case wait before surfacing a
    /// concurrency error.
    /// </summary>
    [Range(1, 500)]
    public int LockRetryDelayMs { get; set; } = 50;

    /// <summary>
    /// Maximum lock-acquisition attempts before surfacing a concurrency error.
    /// Default 20 * 50&#xa0;ms = 1&#xa0;s worst-case wait.
    /// </summary>
    [Range(1, 100)]
    public int LockMaxRetries { get; set; } = 20;
}
