namespace Invoicing.Infrastructure.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Invoicing bounded context. Mirrors the Ordering
/// precedent (<c>services/Ordering/Ordering.Infrastructure/Common/Authorization/AuthPolicies.cs</c>):
/// constants live in the Infrastructure layer so the policy DI registration and the
/// FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol,
/// eliminating typo drift.
/// </summary>
/// <remarks>
/// The plain admin role is realised today as the Keycloak realm role <c>admin</c>
/// (see <see cref="Roles.Admin"/>). When ADR-0010's scope-based gating lands (v2+),
/// this policy will be augmented with a <c>RequireClaim("scope", "invoicing.admin.*")</c>
/// assertion alongside the role check; the policy name stays stable so endpoints don't
/// need to change.
/// </remarks>
public static class AuthPolicies
{
    /// <summary>
    /// Gates Invoicing admin-only endpoints — currently <c>POST /api/v1/invoicing/invoices/{id}/resend</c>.
    /// </summary>
    public const string InvoicingAdmin = "InvoicingAdmin";
}
