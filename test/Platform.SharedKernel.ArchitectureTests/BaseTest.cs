using System.Reflection;
using Platform.SharedKernel.Base;

namespace Platform.SharedKernel.ArchitectureTests;

/// <summary>
/// Shared base for Platform.SharedKernel architecture tests. Anchors the shared-kernel assembly
/// via the <see cref="ValueObject"/> base type (which is stable and lives in the target assembly,
/// so no <c>IAssemblyMarker</c> is required). Mirrors the per-BC arch-test convention in
/// <c>test/Catalog.ArchitectureTests/BaseTest.cs</c>.
/// </summary>
public abstract class BaseTest
{
    protected static readonly Assembly PlatformAssembly = typeof(ValueObject).Assembly;
}
