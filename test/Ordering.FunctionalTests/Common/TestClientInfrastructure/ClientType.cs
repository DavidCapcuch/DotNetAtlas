namespace Ordering.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Caller archetypes the Ordering API recognises in functional tests.
/// </summary>
public enum ClientType
{
    /// <summary>No <c>Authorization</c> header — should yield 401 on every protected endpoint.</summary>
    NonAuth,

    /// <summary>Authenticated buyer (no admin role).</summary>
    Buyer,

    /// <summary>A different authenticated buyer — used to assert cross-buyer 404s.</summary>
    OtherBuyer,

    /// <summary>Authenticated user with the <c>admin</c> realm role.</summary>
    Admin,
}
