using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.ArchitectureTests.Domain;

/// <summary>
/// Internal ES domain events (the six events that make up <c>StockItem</c>'s stream) are
/// sealed records, end in <c>DomainEvent</c> (the cross-BC convention), and live under
/// <c>Inventory.Domain.StockItems.Events</c>.
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
            "domain events follow the cross-BC '*DomainEvent' naming convention.");
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
    /// Every ES event lives in <c>Inventory.Domain.StockItems.Events</c>. Folder pinned for
    /// predictable discovery + so a stray event under <c>Inventory.Domain.Foo</c> would fail.
    /// </summary>
    [Fact]
    public void DomainEvents_Should_LiveUnder_StockItemsEventsNamespace()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit<DomainEvent>()
            .Should()
            .ResideInNamespace("Inventory.Domain.StockItems.Events")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Inventory's ES events live under 'Inventory.Domain.StockItems.Events' so the " +
            "StockEventSerializer + Fold reducer can locate every event type at one path.");
    }
}
