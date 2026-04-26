using NetArchTest.Rules;
using Platform.CQRS;

namespace Basket.ArchitectureTests.Application;

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
