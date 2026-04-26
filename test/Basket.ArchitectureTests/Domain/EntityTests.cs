using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Basket.ArchitectureTests.Domain;

/// <summary>
/// Architecture tests for Entity classes that are not aggregate roots.
/// Entities share similar constraints as aggregates: sealed, private constructors, external immutability.
/// </summary>
public class EntityTests : BaseTest
{
    [Fact]
    public void Entities_Should_BeSealed()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .DoNotInherit(typeof(AggregateRoot<>)) // Exclude aggregates - they have their own tests
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Entities should be sealed to prevent inheritance from breaking identity semantics");
    }

    [Fact]
    public void Entities_Should_BeImmutableExternally()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .DoNotInherit(typeof(AggregateRoot<>)) // Exclude aggregates - they have their own tests
            .Should()
            .BeImmutableExternally()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Entities should be immutable to enforce invariants through methods");
    }

    [Fact]
    public void Entities_Should_HavePrivateConstructors()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .DoNotInherit(typeof(AggregateRoot<>)) // Exclude aggregates - they have their own tests
            .Should()
            .MeetCustomRule(new PrivateConstructorsRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Entities should have private constructors to enforce factory method creation");
    }
}
