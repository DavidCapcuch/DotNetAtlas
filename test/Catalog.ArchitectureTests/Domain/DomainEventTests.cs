using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.ArchitectureTests.Domain;

/// <summary>
/// Per architecture-tests.md § 1.3, internal domain events must inherit
/// <see cref="DomainEvent"/>, be sealed records, end in <c>DomainEvent</c>, and live under
/// <c>Catalog.Domain.{Aggregate}.Events</c>.
/// </summary>
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
    /// Per architecture-tests.md § 1.3, every internal domain event lives in
    /// <c>Catalog.Domain.{Aggregate}.Events</c>. Keeps event discovery deterministic and pairs
    /// with the folder convention (<c>Products/Events/*.cs</c>,
    /// <c>Categories/Events/*.cs</c>).
    /// </summary>
    [Fact]
    public void DomainEvents_Should_LiveInAggregateEventsNamespace()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<DomainEvent>()
            .Should()
            .ResideInNamespaceMatching(@"^Catalog\.Domain\.\w+\.Events$")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain events should live under 'Catalog.Domain.<Aggregate>.Events' for predictable discovery");
    }
}
