namespace EShop.BFF.Api.Composition;

/// <summary>
/// Detects a fail-safe stale serve for the composed-page endpoints (bff.md § 3.1 / § 3.4). FusionCache's
/// native fail-safe transparently serves the last-good page when a gating upstream is down, but exposes no
/// per-call "this was stale" flag — so the endpoint infers it from the page's own composition timestamp:
/// a fresh entry can be at most <c>soft TTL + jitter</c> old, so any page older than that fresh window was
/// necessarily served from fail-safe (the entry had expired and the gating upstream couldn't refresh it).
/// </summary>
internal static class StaleServePolicy
{
    /// <summary>
    /// Whether a page composed at <paramref name="generatedAtUtc"/> is older at <paramref name="nowUtc"/>
    /// than <paramref name="freshWindow"/> — and so could only have been served from fail-safe.
    /// </summary>
    public static bool WasServedStale(DateTimeOffset generatedAtUtc, DateTimeOffset nowUtc, TimeSpan freshWindow) =>
        nowUtc - generatedAtUtc > freshWindow;
}
