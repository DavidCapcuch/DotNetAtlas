using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Basket.ArchitectureTests.Domain;

public class AggregateRootTests : BaseTest
{
    /// <summary>
    /// Aggregates should only reference other aggregates by ID (FK), not directly.
    /// This maintains clear boundaries between aggregates (different transactional boundaries). <br/>
    /// Cross-aggregate coordination belongs in Application/Domain Services, not inside aggregates. <br/>
    /// Side effects that span multiple aggregates belong to domain event handlers.
    /// </summary>
    /// <remarks>
    /// See "The Aggregate Root or Root Entity pattern" in "Implement a microservice domain model with .NET" <br/>
    /// https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model#:~:text=an%20aggregate%20root.-,In%20order,-to%20maintain%20separation.
    /// </remarks>
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
                .Where(t => t.GetType() != aggregate.GetType())
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
            "and prevent EF Core hydration from raising domain events");
    }

    /// <summary>
    /// Per architecture-tests.md § 1.2, every aggregate exposes at least one
    /// <c>public static</c> factory whose name starts with <c>Create</c> or <c>From</c>. The
    /// factory wraps the private constructor + invariant validation in a <c>Result&lt;T&gt;</c>;
    /// without it, callers would have no Result-pattern entry point.
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
            "Aggregates need at least one public static factory (named 'Create*' or 'From*') so " +
            "callers can validate invariants via Result<T> instead of constructing directly");
    }
}
