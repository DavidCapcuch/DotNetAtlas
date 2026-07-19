using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.ArchitectureTests.Application;

/// <summary>
/// Inventory's read-side multiplexes both projections + outbox emission inside a single class
/// per projection table.
/// There are two: <c>CurrentStockLevelsProjectionDomainEventHandler</c> (writes
/// <c>current_stock_levels</c> + emits <c>StockLevelChanged</c>) and
/// <c>ReservationLifecycleDomainEventHandler</c> (writes <c>reservation_audit</c> + emits the
/// three <c>inventory.reservations</c> events). Both implement <see cref="IDomainEventHandler{T}"/>
/// for multiple events and follow the universal U-D suffix rule
/// (architecture-tests.md § 1.3): every <c>IDomainEventHandler&lt;T&gt;</c> impl ends with
/// <c>DomainEventHandler</c>, with the role name (<c>Projection</c>, <c>Lifecycle</c>) in front.
/// </summary>
/// <remarks>
/// This deviates from Catalog's "one-class-per-event" convention (which uses separate
/// <c>*ProjectionDomainEventHandler</c> + <c>*OutboxPublisherDomainEventHandler</c> classes per
/// event). The deviation is intentional for Inventory because the multiplexed shape keeps the
/// projection upsert + outbox write co-located inside the single
/// <c>EventStoreRepository.AppendAsync</c> dispatch loop, which preserves the
/// same-DbContext-transaction invariant for the ES write path.
/// </remarks>
public class DomainEventHandlerTests : BaseTest
{
    [Fact]
    public void DomainEventHandlers_Should_HaveNameEndingWith_DomainEventHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .Should()
            .HaveNameEndingWith("DomainEventHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Universal rule (architecture-tests.md § 1.3): every IDomainEventHandler<T> impl must " +
            "end with 'DomainEventHandler'. Role precedes the suffix " +
            "(*ProjectionDomainEventHandler, *OutboxPublisherDomainEventHandler, *LifecycleDomainEventHandler).");
    }

    [Fact]
    public void DomainEventHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain-event handlers should be sealed — each handles a fixed set of events with " +
            "deterministic side effects; inheritance would break the same-DbContext-transaction invariant.");
    }

    /// <summary>
    /// Handlers live under <c>Inventory.Application.StockItems</c> — colocated with the only
    /// aggregate. A stray handler under <c>Inventory.Application.Foo</c> would fail.
    /// </summary>
    [Fact]
    public void DomainEventHandlers_Should_LiveUnder_StockItemsNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .Should()
            .ResideInNamespaceMatching(@"^Inventory\.Application\.StockItems(\.\w+)?$")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain-event handlers belong under 'Inventory.Application.StockItems' (or a sub-namespace) " +
            "since StockItem is Inventory's only aggregate. A handler under any other namespace signals " +
            "either a misnamed class or a missing aggregate boundary.");
    }
}
