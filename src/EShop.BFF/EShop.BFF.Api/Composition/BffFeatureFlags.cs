namespace EShop.BFF.Api.Composition;

/// <summary>OpenFeature flag keys the BFF reads (ADR-0014). Mirror the keys in <c>flags.json</c>.</summary>
internal static class BffFeatureFlags
{
    /// <summary>
    /// Kill-switch (default ON) for the startup home-page cache warm. OFF makes
    /// <see cref="HomePageCacheWarmer"/> skip cleanly — no half-baked state (bff.md § 3.4, ADR-0014).
    /// </summary>
    public const string HomePageEagerCacheWarm = "bff.home-page-eager-cache-warm";
}
