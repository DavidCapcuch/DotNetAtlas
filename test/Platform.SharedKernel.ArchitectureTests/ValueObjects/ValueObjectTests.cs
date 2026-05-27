using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Platform.SharedKernel.ArchitectureTests.ValueObjects;

/// <summary>
/// See https://jscarle.dev/save-your-reputation-build-better-value-objects-in-net/.
/// Mirrors the per-BC arch-test set (test/Catalog.ArchitectureTests/Domain/ValueObjectTests.cs)
/// applied to <c>Platform.SharedKernel</c> — the BC tests scan only their own Domain assembly,
/// so without this project's rules the shared-kernel VOs (<c>Money</c>, <c>Address</c>) had no
/// drift guard. Adding any future <c>ValueObject</c> subtype to the shared kernel must satisfy
/// these three rules; otherwise this build fails.
/// </summary>
public class ValueObjectTests : BaseTest
{
    /// <summary>
    /// Sealed value objects preserve equality semantics — inheritance breaks them.
    /// </summary>
    [Fact]
    public void ValueObjects_Should_BeSealed()
    {
        var result = Types.InAssembly(PlatformAssembly)
            .That()
            .Inherit<ValueObject>()
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Value objects should be sealed - inheritance breaks equality semantics");
    }

    /// <summary>
    /// Value objects are immutable — once created their state never changes. This guarantees
    /// thread safety, safe sharing, and consistent equality.
    /// </summary>
    [Fact]
    public void ValueObjects_Should_BeImmutable()
    {
        var result = Types.InAssembly(PlatformAssembly)
            .That()
            .Inherit<ValueObject>()
            .Should()
            .BeImmutable()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Value objects should be immutable - once created, their state should never change. " +
            "This ensures thread safety, safe sharing, and consistent equality semantics");
    }

    /// <summary>
    /// Factory methods (returning <c>Result&lt;T&gt;</c>) are the only sanctioned construction
    /// path so validation can run. Public ctors would let callers bypass invariants.
    /// </summary>
    [Fact]
    public void ValueObjects_Should_NotHavePublicConstructor()
    {
        var result = Types.InAssembly(PlatformAssembly)
            .That()
            .Inherit<ValueObject>()
            .Should()
            .NotHavePublicConstructor()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Value objects should have private constructors to enforce factory method creation");
    }
}
