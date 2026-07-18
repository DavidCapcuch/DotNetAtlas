namespace Catalog.IntegrationTests.Common.TestClientInfrastructure;

/// <summary>
/// Token shapes mirrored against the Catalog scope-policy pair (ADR-0010).
/// </summary>
public enum ClientType
{
    /// <summary>No Authorization header.</summary>
    NonAuth,

    /// <summary>Token carrying <c>scope: catalog.read</c> only — satisfies the read policy, fails on write.</summary>
    ReadOnly,

    /// <summary>
    /// Human-admin token carrying the <c>admin</c> realm role AND
    /// <c>scope: catalog.read catalog.write</c> — satisfies both the read and the
    /// (role + scope) write policy.
    /// </summary>
    WriteAdmin,

    /// <summary>
    /// Token carrying <c>scope: catalog.read catalog.write</c> but NO <c>admin</c> role —
    /// satisfies the read policy, fails the write policy on the missing role. Pins the role
    /// half of the defense-in-depth write gate so it can't be silently dropped.
    /// </summary>
    WriteScopeNoAdmin,
}
