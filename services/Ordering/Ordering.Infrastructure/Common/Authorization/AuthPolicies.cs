namespace Ordering.Infrastructure.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Ordering bounded context. Mirrors the
/// Weather precedent (<c>src/Weather.Infrastructure/Common/Authorization/AuthPolicies.cs</c>):
/// constants live in the Infrastructure layer so the policy DI registration
/// and the FastEndpoints <c>Policies(...)</c> attribute reference the same
/// string by symbol, eliminating "OrderingAdmin" typo drift.
/// </summary>
/// <remarks>
/// <para>
/// The plain admin role is realised today as the Keycloak realm role
/// <c>admin</c> (see <see cref="Roles.Admin"/>). When ADR-0010's scope-based
/// gating lands (v2+), this policy will be augmented with a
/// <c>RequireClaim("scope", "ordering.commands.*")</c> assertion alongside
/// the role check; the policy name stays stable so endpoints don't need to
/// change.
/// </para>
/// <para>
/// The application layer documents this constant by name in the saga-aware
/// command XML-doc comments (e.g.,
/// <c>Ordering.Application.Orders.CancelOrder.CancelOrderCommand</c>).
/// </para>
/// </remarks>
public static class AuthPolicies
{
    /// <summary>
    /// Gates Ordering admin-only endpoints — <c>POST /api/v1/ordering/orders/{id}/ship</c>,
    /// <c>POST /api/v1/ordering/orders/{id}/deliver</c>, and the admin branch of
    /// <c>POST /api/v1/ordering/orders/{id}/cancel</c>.
    /// </summary>
    public const string OrderingAdmin = "OrderingAdmin";
}
