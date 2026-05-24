using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.ArchitectureTests.Application;

/// <summary>
/// Domain event handlers are sealed and follow one of the BC's two naming conventions for
/// predictable discovery and DI scanning.
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
            .Or().HaveNameEndingWith("OutboxPublisherDomainEventHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Domain event handlers must end with 'ProjectionHandler' (read-view upserts), " +
            "'OutboxPublisher' (short form), or 'OutboxPublisherDomainEventHandler' (long form)");
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
