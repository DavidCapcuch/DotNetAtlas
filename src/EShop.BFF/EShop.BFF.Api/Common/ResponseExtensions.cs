using Microsoft.AspNetCore.Http;

namespace EShop.BFF.Api.Common;

/// <summary>Response-header helpers shared by the composed-page endpoints (bff.md § 3.1 / § 3.4 / § 2.4).</summary>
internal static class ResponseExtensions
{
    /// <summary>Header signalling the body was served with stale data (bff.md § 3.1 / § 3.4 failure tables).</summary>
    public const string StaleHeader = "X-BFF-Stale";

    /// <summary>
    /// Sets <c>X-BFF-Stale: true</c> when the composed response carries stale data — a fail-safe stale serve
    /// (gating upstream down) or a partial-degraded compose (a non-gating overlay missing). Mirrors the body's
    /// <c>HasStaleData</c> (bff.md § 2.4); orthogonal to <c>X-BFF-PartialData</c>, which names <em>which</em>
    /// source was lost.
    /// </summary>
    public static void SignalStale(this HttpResponse response, bool hasStaleData)
    {
        if (hasStaleData)
        {
            response.Headers[StaleHeader] = "true";
        }
    }
}
