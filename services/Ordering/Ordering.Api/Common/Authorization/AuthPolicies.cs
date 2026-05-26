namespace Ordering.Api.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Ordering bounded context. Constants live in the Api
/// layer so the policy DI registration (<see cref="AuthenticationDependencyInjection"/>) and
/// the FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol,
/// eliminating "OrderingAdmin" typo drift.
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
internal static class AuthPolicies
{
    /// <summary>
    /// Gates Ordering admin-only endpoints — <c>POST /api/v1/ordering/orders/{id}/ship</c>,
    /// <c>POST /api/v1/ordering/orders/{id}/deliver</c>, and the admin branch of
    /// <c>POST /api/v1/ordering/orders/{id}/cancel</c>.
    /// </summary>
    public const string OrderingAdmin = "OrderingAdmin";
}
