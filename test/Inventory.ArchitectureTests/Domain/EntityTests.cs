using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Inventory.ArchitectureTests.Domain;

/// <summary>
/// Architecture tests for <see cref="Entity{TId}"/> classes that are NOT aggregate roots.
/// Inventory currently has no non-aggregate <see cref="Entity{TId}"/> subclasses (reservation
/// state lives as a value-typed <c>ReservationInfo</c> record inside the <c>StockItem</c>
/// aggregate, and projection rows are POCOs), so these tests trivially pass on an empty type
/// set. The file lands so any future non-aggregate Entity stays disciplined the moment it
/// appears. Mirrors <c>Catalog.ArchitectureTests.Domain.EntityTests</c>.
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
            .DoNotInherit(typeof(AggregateRoot<>))
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
            .DoNotInherit(typeof(AggregateRoot<>))
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
            .DoNotInherit(typeof(AggregateRoot<>))
            .Should()
            .MeetCustomRule(new PrivateConstructorsRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Entities should have private constructors to enforce factory method creation");
    }
}
