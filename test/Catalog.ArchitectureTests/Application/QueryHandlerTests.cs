using NetArchTest.Rules;
using Platform.CQRS;

namespace Catalog.ArchitectureTests.Application;

/// <summary>
/// Per architecture-tests.md § 1.4, query handlers are named <c>*QueryHandler</c> and sealed.
/// </summary>
public class QueryHandlerTests : BaseTest
{
    [Fact]
    public void QueryHandlers_Should_HaveNameEndingWith_QueryHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .HaveNameEndingWith("QueryHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Query handlers should follow the naming convention '*QueryHandler' for easy discovery and consistency");
    }

    [Fact]
    public void QueryHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Query handlers should be sealed - each encapsulates a single use case and inheritance would " +
            "break the single responsibility principle");
    }
}
