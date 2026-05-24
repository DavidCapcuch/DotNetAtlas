using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Invoicing.ArchitectureTests.Domain;

public sealed class AggregateRootTests : BaseTest
{
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
            "Aggregates must be sealed so invariant-bearing methods cannot be overridden");
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
            "Aggregate state changes must go through guarded methods, not public setters");
    }

    [Fact]
    public void AggregateRoots_Should_HavePrivateConstructorsOnly()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .MeetCustomRule(new PrivateConstructorsRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Aggregates must hide their constructors so creation goes through static factories");
    }

    [Fact]
    public void AggregateRoots_Should_ExposeStaticFactoryMethod()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .MeetCustomRule(new HasPublicStaticFactoryMethodRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Aggregates must expose at least one public static Create*/From* factory");
    }
}
