namespace Payments.Infrastructure.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Payments bounded context. Match
/// the role strings in <c>src/keycloak/realm-export.json</c>.
/// </summary>
public static class Roles
{
    /// <summary>
    /// Operator / fulfilment-support role — backs <see cref="AuthPolicies.PaymentsAdmin"/>.
    /// </summary>
    public const string Admin = "admin";
}
