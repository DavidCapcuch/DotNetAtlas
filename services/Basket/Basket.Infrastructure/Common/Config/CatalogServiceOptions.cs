using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Strongly-typed configuration for the outbound HTTP client that the Basket
/// Anti-Corruption Layer uses to read product data from the Catalog service
/// (basket.md &#xa7; 9.3). Bound from the <c>Basket:Catalog</c> configuration section.
/// </summary>
public sealed class CatalogServiceOptions
{
    public const string Section = "Basket:Catalog";

    /// <summary>
    /// Absolute base URL pointing at Catalog's HTTP endpoint (typically the YARP
    /// edge). Service-to-service traffic still flows through YARP per ADR-0010;
    /// no direct pod-to-pod URLs.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Per-request timeout on the typed <see cref="HttpClient"/>. Default 2&#xa0;s
    /// (basket.md &#xa7; 9.3) — YARP handles cross-service retries at the edge, so
    /// this timeout is the single source of truth for how long Basket waits on
    /// Catalog before surfacing <c>BasketErrors.CatalogUnavailable</c>.
    /// </summary>
    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 2;

    /// <summary>
    /// OAuth2 scope requested from Keycloak (ADR-0010) for every outbound call
    /// to Catalog. Default <c>catalog.read</c> — overridable so ops can rotate
    /// scopes without a code change.
    /// </summary>
    [Required]
    public string Scope { get; set; } = "catalog.read";
}
