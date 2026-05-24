using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.ArchitectureTests.Domain;

public sealed class DomainEventTests : BaseTest
{
    [Fact]
    public void DomainEvents_Should_HaveNameEndingWith_DomainEvent()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().Inherit<DomainEvent>()
            .Should().HaveNameEndingWith("DomainEvent")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain events follow the naming convention '*DomainEvent'");
    }

    [Fact]
    public void DomainEvents_Should_BeSealed()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().Inherit<DomainEvent>()
            .Should().BeSealed()
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain events are immutable contracts and must be sealed");
    }

    [Fact]
    public void DomainEvents_Should_BeImmutable()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().Inherit<DomainEvent>()
            .Should().BeImmutable()
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain events should be immutable - use init-only or private setters, not public setters");
    }

    [Fact]
    public void DomainEvents_Should_LiveIn_AggregateEventsNamespace()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().Inherit<DomainEvent>()
            .Should().ResideInNamespaceMatching(@"^Ordering\.Domain\.\w+\.Events$")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain events live under <Aggregate>.Events for discoverability (architecture-tests.md § 1.3)");
    }
}
