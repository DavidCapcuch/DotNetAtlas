using NetArchTest.Rules;
using Platform.CQRS;

namespace Invoicing.ArchitectureTests.Application;

public sealed class QueryHandlerTests : BaseTest
{
    [Fact]
    public void QueryHandlers_Should_HaveNameEndingWith_QueryHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ImplementInterface(typeof(IQueryHandler<,>))
            .Should().HaveNameEndingWith("QueryHandler")
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void QueryHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ImplementInterface(typeof(IQueryHandler<,>))
            .Should().BeSealed()
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }
}
