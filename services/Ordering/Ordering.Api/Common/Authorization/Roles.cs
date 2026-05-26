namespace Ordering.Api.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Ordering bounded context.
/// Match the role strings in <c>src/keycloak/realm-export.json</c>.
/// </summary>
internal static class Roles
{
    /// <summary>
    /// Buyer-support / fulfilment-operator role. Backs
    /// <see cref="AuthPolicies.OrderingAdmin"/>.
    /// </summary>
    public const string Admin = "admin";
}
