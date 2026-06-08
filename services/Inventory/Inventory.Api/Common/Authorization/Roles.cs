namespace Inventory.Api.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Inventory bounded context.
/// Match the role strings in <c>src/keycloak/realm-export.json</c>.
/// </summary>
internal static class Roles
{
    /// <summary>
    /// Admin / ops role. Backs <see cref="AuthPolicies.AdminReadPolicy"/> (reservation-audit
    /// reads) and the write half of <see cref="AuthPolicies.WritePolicy"/> (Receive / Adjust) —
    /// both require this role on top of the relevant scope.
    /// </summary>
    public const string Admin = "admin";
}
