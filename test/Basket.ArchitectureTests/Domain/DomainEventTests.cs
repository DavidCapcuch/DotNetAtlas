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

    /// <summary>
    /// Per architecture-tests.md § 1.3, every internal domain event lives under
    /// <c>Basket.Domain.&lt;Aggregate&gt;.Events</c> for predictable discovery and to pair
    /// with the per-aggregate folder convention.
    /// </summary>
    [Fact]
    public void DomainEvents_Should_LiveInAggregateEventsNamespace()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<DomainEvent>()
            .Should()
            .ResideInNamespaceMatching(@"^Basket\.Domain\.\w+\.Events$")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain events should live under 'Basket.Domain.<Aggregate>.Events' for predictable discovery");
    }
}
