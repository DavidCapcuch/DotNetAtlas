using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Ordering.ArchitectureTests.Domain;

/// <summary>
/// See https://jscarle.dev/save-your-reputation-build-better-value-objects-in-net/.
/// </summary>
public class ValueObjectTests : BaseTest
{
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
