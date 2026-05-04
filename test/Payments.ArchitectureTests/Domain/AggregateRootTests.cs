using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Payments.ArchitectureTests.Domain;

/// <summary>
/// Per architecture-tests.md § 1.2, enforces aggregate discipline on
/// <see cref="Payments.Domain.Transactions.PaymentTransaction"/>: sealed, externally immutable,
/// constructed only via private ctor + public static factory. (Payments has a single aggregate
/// so the cross-aggregate-by-id rule from Catalog is omitted.)
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
    /// Factory methods are the only sanctioned construction path; the parameterless ctor exists
    /// only for EF Core hydration and must be private so it cannot raise domain events.
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
