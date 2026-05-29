namespace Inventory.Api.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Inventory bounded context.
/// Match the role strings in <c>src/keycloak/realm-export.json</c>.
/// </summary>
internal static class Roles
{
    /// <summary>
    /// Admin / ops role. Backs the write half of
    /// <see cref="AuthPolicies.WritePolicy"/> (Receive / Adjust),
    /// which requires this role AND the <c>inventory.write</c> scope.
    /// </summary>
    public const string Admin = "admin";
}
