namespace Ordering.Api.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Ordering bounded context. Constants live in the Api
/// layer so the policy DI registration (<see cref="AuthenticationDependencyInjection"/>) and
/// the FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol,
/// eliminating "OrderingAdmin" typo drift.
/// </summary>
/// <remarks>
/// <para>
/// This gate is <b>role-only by design</b>, not transitionally: ship/deliver are pure
/// human-admin actions with no service caller (order state changes arrive over Kafka), so no
/// <c>ordering.write</c> scope is defined — inventing one only the swagger client would ever
/// request would be "provisioned-for-someday" dead config (ADR-0010 §"Role vs scope canonical
/// model"). The <c>admin</c> realm role is <see cref="Roles.Admin"/>.
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
