using System.ComponentModel.DataAnnotations;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// Options describing this service's OAuth2 client-credentials identity against Keycloak (ADR-0010).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Authority"/> is the Keycloak realm URL (for example
/// <c>http://keycloak:8080/realms/dotnetatlas</c>). The handler appends
/// <c>/protocol/openid-connect/token</c> when acquiring a token.
/// </para>
/// </remarks>
public sealed class ServiceAuthOptions
{
    /// <summary>Configuration section name: <c>ServiceAuth</c>.</summary>
    public const string Section = "ServiceAuth";

    /// <summary>
    /// Name of the named <see cref="HttpClient"/> used internally to call the Keycloak token
    /// endpoint. Stable so the DI extension and the handler agree on the client name.
    /// </summary>
    public const string TokenEndpointHttpClientName = "ServiceAuth.TokenEndpoint";

    /// <summary>Keycloak realm URL (no trailing segments).</summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    /// <summary>Keycloak client-id for this service.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Keycloak client-secret. Sourced from env var
    /// <c>KEYCLOAK__SERVICE_CLIENT_SECRET__&lt;service&gt;</c> in production (ADR-0010 line 88).
    /// </summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Logical name of this service. Used as the cache key partition for outbound
    /// client-credentials tokens (see <c>ClientCredentialsTokenHandler</c>). Inbound JWT
    /// audience is configured separately under
    /// <c>Authentication:JwtBearer:TokenValidationParameters:ValidAudience</c>.
    /// </summary>
    [Required]
    public string ServiceName { get; set; } = string.Empty;
}
