using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.ArchitectureTests.Application;

/// <summary>
/// Catalog uses two distinct <see cref="IDomainEventHandler{T}"/> tracks: read-side
/// <c>*ProjectionDomainEventHandler</c> classes (one per internal event) and
/// <c>*OutboxPublisherDomainEventHandler</c> classes (4 external Avro events). Both follow the
/// universal U-D suffix rule (architecture-tests.md § 1.3): the role name
/// (<c>Projection</c>, <c>OutboxPublisher</c>) precedes the mandatory <c>DomainEventHandler</c>
/// suffix. Both must be sealed. This Catalog-specific rule sharpens the universal rule by
/// pinning Catalog's two-role taxonomy — a future <c>NotificationDomainEventHandler</c> would
/// fail here and require a deliberate widening of the read-side closure.
/// </summary>
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
    public void DomainEventHandlers_Should_HaveNameEndingWith_ProjectionDomainEventHandlerOrOutboxPublisherDomainEventHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .Should()
            .HaveNameEndingWith("ProjectionDomainEventHandler")
            .Or().HaveNameEndingWith("OutboxPublisherDomainEventHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain event handlers in Catalog must end with 'ProjectionDomainEventHandler' " +
            "(read-view upserts) or 'OutboxPublisherDomainEventHandler' (external Avro events). " +
            "Both suffixes satisfy the universal U-D rule (architecture-tests.md § 1.3) and pin " +
            "Catalog's deliberate two-role read-side closure.");
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
            "Domain event handlers should be sealed - each encapsulates a single side effect and " +
            "inheritance would break the single-responsibility principle");
    }
}
