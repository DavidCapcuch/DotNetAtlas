using DotNetAtlas.CQS;
using NetArchTest.Rules;

namespace DotNetAtlas.ArchitectureTests.Application;

/// <summary>
/// Architecture tests for query handlers.
/// </summary>
public class QueryHandlerTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
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

    /// <summary>
    /// Handlers should be sealed to prevent inheritance.
    /// Each handler encapsulates a single use case.
    /// </summary>
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
