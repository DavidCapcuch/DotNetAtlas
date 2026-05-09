namespace Invoicing.Infrastructure.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Invoicing bounded context. Match the role
/// strings in <c>src/keycloak/realm-export.json</c>.
/// </summary>
public static class Roles
{
    /// <summary>
    /// Buyer-support / fulfilment-operator role. Backs <see cref="AuthPolicies.InvoicingAdmin"/>.
    /// </summary>
    public const string Admin = "admin";
}
