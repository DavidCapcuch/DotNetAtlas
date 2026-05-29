namespace Catalog.Api.Common.Authorization;

/// <summary>
/// OAuth2 scope names the Catalog BC requires on inbound bearer tokens (ADR-0010).
/// Keycloak emits these in the space-separated <c>scope</c> claim; the policy
/// registration in <see cref="AuthenticationDependencyInjection"/> checks membership.
/// </summary>
internal static class Scopes
{
    /// <summary>Read-only access to Catalog queries.</summary>
    public const string CatalogRead = "catalog.read";

    /// <summary>Admin write access to Catalog commands. Implies <see cref="CatalogRead"/>.</summary>
    public const string CatalogWrite = "catalog.write";
}
