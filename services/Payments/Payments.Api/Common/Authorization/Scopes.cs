namespace Payments.Api.Common.Authorization;

/// <summary>
/// OAuth2 scope names the Payments BC requires on inbound bearer tokens. Per
/// <see href="../../../../../../docs/adr/0010-service-to-service-auth.md#implementation-notes">ADR-0010 § Implementation Notes — Scope enforcement on inbound HTTP</see>
/// the admin GET surface is gated on the <c>payments.read</c> scope in addition
/// to the realm role — the scope claim originates from the Keycloak
/// <c>payments-service</c> client mapper.
/// </summary>
internal static class Scopes
{
    /// <summary>
    /// Read-only access to Payments admin queries. Required by
    /// <see cref="AuthPolicies.PaymentsAdmin"/>. Defined in
    /// <see href="../../../../../../docs/adr/0010-service-to-service-auth.md#implementation-notes">ADR-0010 § Implementation Notes — Scope enforcement on inbound HTTP</see>.
    /// </summary>
    public const string PaymentsRead = "payments.read";
}
