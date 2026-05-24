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

    /// <summary>
    /// Per architecture-tests.md § 1.2, every aggregate exposes at least one
    /// <c>public static</c> factory whose name starts with <c>Create</c>, <c>From</c>, or
    /// <c>Fold</c> (event-sourcing convention — see Inventory <see cref="BaseTest"/>
    /// divergence note). Without it, callers would have no Result-pattern / rehydration
    /// entry point.
    /// </summary>
    [Fact]
    public void AggregateRoots_Should_HavePublicStaticFactoryMethod()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .MeetCustomRule(new HasPublicStaticFactoryMethodRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Aggregates need at least one public static factory (named 'Create*', 'From*', or " +
            "'Fold*') so callers can validate invariants via Result<T> or rehydrate from an " +
            "event stream instead of constructing directly");
    }

    /// <summary>
    /// Aggregates should only reference other aggregates by ID (FK), not directly.
    /// This maintains crisp transactional boundaries — cross-aggregate coordination belongs in
    /// Application/Domain Services, not inside aggregates. Self-skipping when the BC currently
    /// has a single aggregate (Inventory's <c>StockItem</c>), but still load-bearing as soon as
    /// a second aggregate ships.
    /// </summary>
    [Fact]
    public void AggregateRoots_ShouldNot_ReferenceOtherAggregatesByType()
    {
        var aggregateRootTypes = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .GetTypes()
            .ToList();

        if (aggregateRootTypes.Count < 2)
        {
            return;
        }

        var allFailingTypes = new List<Type>();

        foreach (var aggregate in aggregateRootTypes)
        {
            var otherAggregates = aggregateRootTypes
                .Where(t => t != aggregate)
                .Select(t => t.FullName!)
                .ToArray();

            var result = Types.InAssembly(DomainAssembly)
                .That()
                .HaveName(aggregate.Name)
                .ShouldNot()
                .HaveDependencyOnAny(otherAggregates)
                .GetResult();

            allFailingTypes.AddRange(result.FailingTypes.Select(t => t.ReflectionType));
        }

        allFailingTypes.Should().BeEmpty(
            "Aggregates should only reference other aggregates by ID (FK), not directly. " +
            "This maintains clear boundaries between aggregates with different transactional boundaries");
    }
}
