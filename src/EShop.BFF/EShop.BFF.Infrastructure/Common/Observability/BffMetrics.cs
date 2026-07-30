using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace EShop.BFF.Infrastructure.Common.Observability;

/// <summary>
/// The BFF's custom OpenTelemetry instrumentation (bff.md § 2.4): the <c>EShop.BFF</c> meter and the
/// per-endpoint counters + request-span tags that the generic FusionCache / HttpClient instrumentation
/// can't express. Collected automatically by the <c>AddMeter("*")</c> wildcard
/// (<see cref="ObservabilityDependencyInjection"/>) — no explicit registration needed.
/// </summary>
public static class BffMetrics
{
    /// <summary>Meter name, pinned by bff.md § 2.4 (NOT <c>ApplicationInfo.AppName</c>, which is "BFF").</summary>
    public const string MeterName = "EShop.BFF";

    /// <summary>The <c>bff.endpoint</c> tag value for the home page (<c>GET /api/v1/bff/home-page</c>).</summary>
    public const string HomePageEndpoint = "home-page";

    /// <summary>The <c>bff.endpoint</c> tag value for the product page (<c>GET /api/v1/bff/product-page/{id}</c>).</summary>
    public const string ProductPageEndpoint = "product-page";

    /// <summary>The <c>bff.endpoint</c> tag value for the basket page (<c>GET /api/v1/bff/basket</c>).</summary>
    public const string BasketEndpoint = "basket";

    /// <summary>Tag key carrying the logical BFF endpoint name on counters + the request span.</summary>
    public const string EndpointTag = "bff.endpoint";

    /// <summary>Tag key carrying the upstream service name on upstream-scoped counters.</summary>
    public const string UpstreamTag = "bff.upstream";

    /// <summary>Request-span tag key: did the BFF return from cache without composing? (bool)</summary>
    public const string CacheHitTag = "bff.cache.hit";

    /// <summary>Request-span tag key: was the response served with <c>HasStaleData: true</c>? (bool)</summary>
    public const string StaleTag = "bff.stale";

    private static readonly Meter Meter = new(MeterName, ApplicationInfo.Version);

    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "bff.cache.hits",
        unit: "{hits}",
        description: "BFF composed-response cache hits, tagged by endpoint (the per-endpoint split FusionCache's shared-instance counter can't give).");

    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "bff.cache.misses",
        unit: "{misses}",
        description: "BFF composed-response cache misses (the FusionCache factory ran), tagged by endpoint.");

    private static readonly Counter<long> PartialResponses = Meter.CreateCounter<long>(
        "bff.partial_response",
        unit: "{responses}",
        description: "BFF 200s served with partial/degraded data (an upstream failed but the page still rendered), tagged by endpoint. Nothing else emits this — the canonical degraded-UX signal (bff.md § 2.4).");

    private static readonly Counter<long> UnbindablePayloads = Meter.CreateCounter<long>(
        "bff.upstream.unbindable_payload",
        unit: "{responses}",
        description: "Upstream 2xx responses the BFF could not bind to its anti-corruption record, tagged by upstream — a contract change, not an outage (bff.md § 4).");

    /// <summary>
    /// Records that <paramref name="upstream"/> answered successfully with a payload the BFF could not
    /// bind. Strict binding routes that into the same degradation an unreachable upstream produces
    /// (bff.md § 4), so a dashboard cannot otherwise tell a contract change from an outage — only the
    /// exception type in the log can. This counter is that distinction.
    /// </summary>
    /// <param name="cause">
    /// The caught failure. Clients catch every upstream failure mode in one block, so this method
    /// classifies rather than asking each call site to: it counts <see cref="JsonException"/> — the
    /// binding failure — and is a deliberate no-op for transport, timeout and circuit-open causes,
    /// which the resilience instrumentation already covers.
    /// </param>
    public static void RecordUnbindablePayload(string upstream, Exception cause)
    {
        if (cause is JsonException)
        {
            UnbindablePayloads.Add(1, new KeyValuePair<string, object?>(UpstreamTag, upstream));
        }
    }

    /// <summary>
    /// Records one composed-response cache outcome for <paramref name="endpoint"/>: a hit (served from
    /// cache without composing) or a miss (the FusionCache factory ran).
    /// </summary>
    public static void RecordCache(string endpoint, bool hit)
    {
        var counter = hit ? CacheHits : CacheMisses;
        counter.Add(1, new KeyValuePair<string, object?>(EndpointTag, endpoint));
    }

    /// <summary>
    /// Records that <paramref name="endpoint"/> returned a partial 200 — an upstream failed but the page
    /// still rendered with <c>X-BFF-PartialData</c>. Counted on <em>every</em> partial 200 served (cache
    /// hit or miss), so the rate reflects ongoing degraded UX, not one spike per recompose (bff.md § 2.4).
    /// </summary>
    public static void RecordPartialResponse(string endpoint) =>
        PartialResponses.Add(1, new KeyValuePair<string, object?>(EndpointTag, endpoint));

    /// <summary>
    /// Enriches the current request span (<see cref="Activity.Current"/>) with the per-endpoint tags from
    /// bff.md § 2.4: <c>bff.endpoint</c>, <c>bff.cache.hit</c>, and <c>bff.stale</c>. A no-op when no span
    /// is active (e.g. the eager-warm path, or tests without tracing).
    /// </summary>
    public static void TagRequest(string endpoint, bool cacheHit, bool stale)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.SetTag(EndpointTag, endpoint);
        activity.SetTag(CacheHitTag, cacheHit);
        activity.SetTag(StaleTag, stale);
    }
}
