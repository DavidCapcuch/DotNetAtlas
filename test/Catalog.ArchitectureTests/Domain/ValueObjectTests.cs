using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Catalog.ArchitectureTests.Domain;

/// <summary>
/// See https://jscarle.dev/save-your-reputation-build-better-value-objects-in-net/.
/// </summary>
public class ValueObjectTests : BaseTest
{
    /// <summary>
    /// Sealed value objects preserve equality semantics — inheritance breaks them.
    /// </summary>
    [Fact]
    public void ValueObjects_Should_BeSealed()
    {
        var result = Types.InAssembly(DomainAssembly)
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
        var result = Types.InAssembly(DomainAssembly)
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
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<ValueObject>()
            .Should()
            .NotHavePublicConstructor()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Value objects should have private constructors to enforce factory method creation");
    }
}
