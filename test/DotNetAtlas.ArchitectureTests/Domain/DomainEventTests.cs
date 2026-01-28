using DotNetAtlas.SharedKernel.Base.DomainEvents;
using NetArchTest.Rules;

namespace DotNetAtlas.ArchitectureTests.Domain;

public class DomainEventTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
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

    /// <summary>
    /// Sealed events prevent inheritance which could break event contracts.
    /// </summary>
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
