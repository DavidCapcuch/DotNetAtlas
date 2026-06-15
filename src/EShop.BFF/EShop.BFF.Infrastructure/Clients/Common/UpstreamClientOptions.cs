using System.ComponentModel.DataAnnotations;

namespace EShop.BFF.Infrastructure.Clients.Common;

/// <summary>
/// Bound configuration for one upstream typed HTTP client (bff.md § 4). The per-attempt and
/// total-request timeouts are owned by the shared resilience pipeline (<see cref="BffResilience"/>,
/// bff.md § 2.1), not by <see cref="System.Net.Http.HttpClient.Timeout"/>.
/// </summary>
internal abstract class UpstreamClientOptions
{
    /// <summary>Absolute base URL of the upstream service (e.g. the YARP edge or the service host).</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>OAuth2 scope for the <c>client_credentials</c> service token (ADR-0010). The callee
    /// audience rides this scope.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;
}
