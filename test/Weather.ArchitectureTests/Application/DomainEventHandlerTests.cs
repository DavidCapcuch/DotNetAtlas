using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Weather.ArchitectureTests.Application;

/// <summary>
/// Architecture tests for domain event handlers.
/// </summary>
public class DomainEventHandlerTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
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
            "Domain event handlers should follow the naming convention '*DomainEventHandler' " +
            "for easy discovery and to distinguish them from command/query handlers");
    }
}
