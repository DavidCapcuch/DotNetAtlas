namespace Payments.Api.Common.Authorization;

/// <summary>
/// Keycloak realm-role names recognised by the Payments bounded context. Match
/// the role strings in <c>src/keycloak/realm-export.json</c>. Decision anchor:
/// <see href="../../../../../../docs/adr/0010-service-to-service-auth.md#implementation-notes">ADR-0010 § Implementation Notes — Scope enforcement on inbound HTTP</see>.
/// </summary>
internal static class Roles
{
    /// <summary>
    /// Operator / fulfilment-support role — backs <see cref="AuthPolicies.PaymentsAdmin"/>.
    /// Defined in <see href="../../../../../../docs/adr/0010-service-to-service-auth.md#implementation-notes">ADR-0010 § Implementation Notes — Scope enforcement on inbound HTTP</see>.
    /// </summary>
    public const string Admin = "admin";
}
