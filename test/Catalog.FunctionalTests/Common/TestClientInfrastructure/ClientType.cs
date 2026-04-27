namespace Catalog.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Token shapes mirrored against the Catalog scope-policy pair (ADR-0010).
/// </summary>
public enum ClientType
{
    /// <summary>No Authorization header.</summary>
    NonAuth,

    /// <summary>Token carrying <c>scope: catalog.read</c> only — satisfies the read policy, fails on write.</summary>
    ReadOnly,

    /// <summary>Token carrying <c>scope: catalog.read catalog.write</c> — satisfies both policies.</summary>
    WriteAdmin,
}
