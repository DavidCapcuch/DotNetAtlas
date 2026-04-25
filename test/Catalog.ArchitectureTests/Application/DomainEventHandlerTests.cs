using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.ArchitectureTests.Application;

/// <summary>
/// Catalog uses two distinct <see cref="IDomainEventHandler{T}"/> tracks: read-side
/// <c>*ProjectionHandler</c> classes (one per internal event, see catalog-m4.md M3.6) and
/// <c>*OutboxPublisher</c> classes (4 external Avro events). Both are sealed; both must follow
/// one of the two suffix conventions for predictable discovery and DI scanning.
/// </summary>
public class DomainEventHandlerTests : BaseTest
{
    [Fact]
    public void DomainEventHandlers_Should_HaveNameEndingWith_ProjectionHandlerOrOutboxPublisher()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .Should()
            .HaveNameEndingWith("ProjectionHandler")
            .Or().HaveNameEndingWith("OutboxPublisher")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain event handlers in Catalog must end with 'ProjectionHandler' (read-view upserts) " +
            "or 'OutboxPublisher' (external Avro events) — see catalog.md <example_design_decision>");
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
