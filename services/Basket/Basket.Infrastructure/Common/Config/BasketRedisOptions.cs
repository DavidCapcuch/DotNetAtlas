using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Strongly-typed configuration for the Basket aggregate's <c>redis-basket</c>
/// persistence path. Bound from the <c>Basket:Redis</c> configuration section.
/// </summary>
public sealed class BasketRedisOptions : IValidatableObject
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
    public int LockRetryDelayMs { get; set; } = 250;

    /// <summary>
    /// Maximum lock-acquisition attempts before surfacing a concurrency error.
    /// Defaults are aligned so <c>LockMaxRetries * LockRetryDelayMs ≈ LockTimeoutSeconds</c>
    /// (20 * 250 ms = 5 s) — otherwise a contender gives up while the holder still
    /// has valid TTL, producing spurious <c>BasketConcurrencyError</c>s under load.
    /// </summary>
    [Range(1, 100)]
    public int LockMaxRetries { get; set; } = 20;

    /// <summary>
    /// Custom rule enforcing <c>LockRetryDelayMs * LockMaxRetries &gt;= LockTimeoutSeconds * 1000</c>.
    /// Without this, the retry budget can be shorter than the lock TTL — a contender
    /// gives up before the holder's lock expires (sum2.H-7).
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var retryBudgetMs = (long)LockRetryDelayMs * LockMaxRetries;
        var lockTtlMs = (long)LockTimeoutSeconds * 1000;
        if (retryBudgetMs < lockTtlMs)
        {
            yield return new ValidationResult(
                $"{nameof(LockRetryDelayMs)} ({LockRetryDelayMs} ms) * {nameof(LockMaxRetries)} " +
                $"({LockMaxRetries}) = {retryBudgetMs} ms must be >= {nameof(LockTimeoutSeconds)} " +
                $"({LockTimeoutSeconds} s = {lockTtlMs} ms) to avoid spurious concurrency errors.",
                new[] { nameof(LockRetryDelayMs), nameof(LockMaxRetries), nameof(LockTimeoutSeconds) });
        }
    }
}
