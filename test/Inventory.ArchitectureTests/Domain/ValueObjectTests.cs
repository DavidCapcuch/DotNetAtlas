using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Inventory.ArchitectureTests.Domain;

/// <summary>
/// Value-object discipline (sealed, immutable, no public ctor). Scope is whatever derives
/// <see cref="ValueObject"/> — the filter below is the rule. (<c>docs/bc-design/inventory.md</c>
/// § 4 catalogues Inventory's value concepts, not all of which derive it.)
/// </summary>
/// <remarks>
/// <c>ReservationStatus</c> + <c>ReleaseReason</c> read like value objects but are plain C#
/// enums, so they do not derive <see cref="ValueObject"/> and are intentionally out of scope.
/// </remarks>
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
