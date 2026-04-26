using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.ArchitectureTests.Domain;

public class DomainEventTests : BaseTest
{
    [Fact]
    public void DomainEvents_Should_HaveNameEndingWith_DomainEvent()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<DomainEvent>()
            .Should()
            .HaveNameEndingWith("DomainEvent")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain events should follow the naming convention '*DomainEvent' for easy discovery and consistency");
    }

    [Fact]
    public void DomainEvents_Should_BeSealed()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<DomainEvent>()
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain events should be sealed - inheritance could break event contracts and handler expectations");
    }

    [Fact]
    public void DomainEvents_Should_BeImmutable()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<DomainEvent>()
            .Should()
            .BeImmutable()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain events should be immutable - use init-only or private setters, not public setters");
    }
}
