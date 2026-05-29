namespace Inventory.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Identity / scope variants for the functional-test HTTP clients. Drives
/// <see cref="FakeTokenCreator"/> and <see cref="HttpClientRegistry{TEntryPoint}"/>.
/// </summary>
public enum ClientType
{
    /// <summary>No Authorization header — exercises the 401 branch.</summary>
    NonAuth,

    /// <summary>JWT carries only the <c>inventory.read</c> scope — read endpoints succeed; write endpoints 403.</summary>
    ReadOnly,

    /// <summary>JWT carries the <c>inventory.write</c> scope — admin write endpoints succeed; read endpoints also succeed via the write→read hierarchy.</summary>
    Commands,
}
