namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// A cached Keycloak client-credentials token and its computed expiry (ADR-0010).
/// </summary>
/// <param name="AccessToken">The raw bearer value to attach to outbound requests.</param>
/// <param name="ExpiresAt">Absolute expiry computed at cache-write time from
/// <c>TimeProvider.GetUtcNow() + TimeSpan.FromSeconds(response.expires_in)</c>. Consumers apply the
/// 30-second buffer at check-time rather than cache-time.</param>
internal sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Override the auto-generated record <c>ToString()</c> so the bearer value never leaks into
    /// log lines, span attributes, or exception messages.
    /// </summary>
    public override string ToString() => $"CachedToken {{ ExpiresAt = {ExpiresAt:O} }}";
}
