using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Inventory.ArchitectureTests.Domain;

/// <summary>
/// Aggregate-root discipline for the event-sourced <c>StockItem</c> aggregate: sealed,
/// externally immutable, private constructor, public-static factory shape.
/// </summary>
public class AggregateRootTests : BaseTest
{
    /// <summary>
    /// Sealed aggregates protect invariants — inheritance can bypass business rules.
    /// </summary>
    [Fact]
    public void AggregateRoots_Should_BeSealed()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Aggregates should be sealed - inheritance breaks encapsulation and can bypass business rules");
    }

    /// <summary>
    /// State changes should go through methods that enforce invariants, not direct property
    /// mutation. <c>private set;</c> or <c>init;</c> only.
    /// </summary>
    [Fact]
    public void AggregateRoots_Should_BeImmutableExternally()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .BeImmutableExternally()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Aggregate state changes should go through methods that enforce invariants, " +
            "not direct property mutation. Use private/init setters instead of public setters");
    }

    /// <summary>
    /// Factory methods (<c>StockItem.Fold</c> for rehydration; the parameterless private ctor
    /// for first-write scenarios) are the only sanctioned construction paths. The parameterless
    /// ctor exists only for the empty-stream path and must be private so it cannot raise
    /// domain events.
    /// </summary>
    [Fact]
    public void AggregateRoots_Should_HavePrivateConstructors()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .MeetCustomRule(new PrivateConstructorsRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Aggregates should have private constructors to enforce factory method creation " +
            "and prevent EF Core / fold hydration from raising domain events");
    }
}
