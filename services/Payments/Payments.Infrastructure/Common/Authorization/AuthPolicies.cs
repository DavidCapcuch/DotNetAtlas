namespace Payments.Infrastructure.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Payments bounded context. Constants live
/// in the Infrastructure layer so the policy DI registration
/// (<see cref="AuthDependencyInjection"/>) and the FastEndpoints
/// <c>Policies(...)</c> attribute reference the same string by symbol — avoids
/// "PaymentsAdmin" typo drift between registration and enforcement.
/// </summary>
/// <remarks>
/// Per ADR-0010, admin Payments routes require both the Keycloak realm role
/// <c>admin</c> (<see cref="Roles.Admin"/>) AND the OAuth scope
/// <c>payments.read</c> (<see cref="Scopes.PaymentsRead"/>). Both checks are
/// enforced inside <see cref="AuthDependencyInjection.AddPaymentsAuth"/>; the
/// endpoint side just names the policy.
/// </remarks>
public static class AuthPolicies
{
    /// <summary>
    /// Gates the Payments admin GET endpoints —
    /// <c>GET /api/v1/payments/{id}</c> and
    /// <c>GET /api/v1/payments?orderId=...</c>.
    /// Requires the <c>admin</c> realm role plus the <c>payments.read</c>
    /// scope claim (defense in depth: stolen scope alone, or admin token for
    /// a different audience without the scope, both fail).
    /// </summary>
    public const string PaymentsAdmin = "PaymentsAdmin";
}
