namespace Catalog.Api.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Catalog bounded context.
/// Match the role strings in <c>src/keycloak/realm-export.json</c>.
/// </summary>
internal static class Roles
{
    /// <summary>
    /// Admin / ops role. Backs the write half of
    /// <see cref="AuthPolicies.WritePolicy"/> (product / category mutations),
    /// which requires this role AND the <c>catalog.write</c> scope
    /// (defense-in-depth for human-admin writes).
    /// </summary>
    public const string Admin = "admin";
}
