using System.ComponentModel.DataAnnotations;

namespace Inventory.Infrastructure.Common.Config;

/// <summary>
/// Tuning for the Inventory-owned read-through stock-availability cache (ADR-0034) on
/// <c>redis-cache</c>. Bound from the <c>StockLevelCache</c> config section.
/// </summary>
public sealed class StockLevelCacheOptions
{
    public const string Section = "StockLevelCache";

    /// <summary>
    /// Normal entry TTL — the common-case staleness bound. After it lapses the next read
    /// rebuilds from the projection (with a healthy DB), so a missed/dropped best-effort
    /// eviction self-heals within this window. Kept short because invalidate-on-projection-update
    /// is the primary freshness mechanism, not the TTL (ADR-0034).
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public required TimeSpan Ttl { get; set; }

    /// <summary>
    /// Enables FusionCache fail-safe: this is a SEPARATE degradation window from <see cref="Ttl"/>.
    /// It only kicks in when the projection-read (factory) itself FAILS (e.g. a Postgres blip) —
    /// then the last good value is served for up to <see cref="FailSafeMaxDuration"/> instead of
    /// erroring. It does not extend normal-case staleness past <see cref="Ttl"/>.
    /// </summary>
    [Required]
    public required bool FailSafeEnabled { get; set; }

    /// <summary>
    /// Max stale-serve window once fail-safe engages on a factory failure (see
    /// <see cref="FailSafeEnabled"/>). Bounds display staleness during a DB outage only.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public required TimeSpan FailSafeMaxDuration { get; set; }
}
