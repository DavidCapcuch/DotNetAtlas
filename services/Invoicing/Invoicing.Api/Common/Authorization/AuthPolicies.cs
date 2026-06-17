namespace Invoicing.Api.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Invoicing bounded context. Constants live in the Api
/// layer so the policy DI registration (<see cref="AuthenticationDependencyInjection"/>) and
/// the FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol,
/// eliminating typo drift.
/// </summary>
/// <remarks>
/// This gate is <b>role-only by design</b>, not transitionally: resend is a pure human-admin
/// action with no service caller (invoice state changes arrive over Kafka), so no
/// <c>invoicing.write</c> scope is defined — inventing one only the swagger client would ever
/// request would be "provisioned-for-someday" dead config (ADR-0010 §"Role vs scope canonical
/// model"). The <c>admin</c> realm role is <see cref="Roles.Admin"/>.
/// </remarks>
internal static class AuthPolicies
{
    /// <summary>
    /// Gates Invoicing admin-only endpoints — currently <c>POST /api/v1/invoicing/invoices/{id}/resend</c>.
    /// </summary>
    public const string InvoicingAdmin = "InvoicingAdmin";
}
