using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.ArchitectureTests.Application;

/// <summary>
/// Universal rule (architecture-tests.md § 1.3): every concrete class implementing
/// <see cref="IDomainEventHandler{T}"/> ends with <c>DomainEventHandler</c>; the role name
/// precedes the suffix (<c>*OutboxPublisherDomainEventHandler</c>, etc.). Both rules below
/// enforce the universal naming and the seal-each-side-effect invariant.
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
