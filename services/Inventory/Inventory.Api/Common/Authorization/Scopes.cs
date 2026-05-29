namespace Inventory.Api.Common.Authorization;

/// <summary>
/// OAuth2 scope names the Inventory BC requires on inbound bearer tokens (ADR-0010).
/// Keycloak emits these in the space-separated <c>scope</c> claim; the policy
/// registration in <see cref="AuthenticationDependencyInjection"/> checks membership.
/// </summary>
internal static class Scopes
{
    /// <summary>Read-only access to Inventory queries (service-to-service, e.g. the BFF).</summary>
    public const string InventoryRead = "inventory.read";

    /// <summary>Write access to Inventory admin commands (Receive / Adjust). Implies <see cref="InventoryRead"/>.</summary>
    public const string InventoryWrite = "inventory.write";
}
