namespace Payments.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Caller archetypes the Payments admin API recognises in functional tests.
/// </summary>
public enum ClientType
{
    /// <summary>No <c>Authorization</c> header — should yield 401 on every protected endpoint.</summary>
    NonAuth,

    /// <summary>Authenticated user without the admin role or <c>payments.read</c> scope. Asserts 403 on admin endpoints.</summary>
    User,

    /// <summary>Authenticated admin token missing the <c>payments.read</c> scope. Asserts the scope check is enforced, not just the role.</summary>
    AdminWithoutScope,

    /// <summary>Authenticated admin token with both the realm role and the <c>payments.read</c> scope. Should pass <see cref="Payments.Infrastructure.Common.Authorization.AuthPolicies.PaymentsAdmin"/>.</summary>
    Admin,
}
