using NetArchTest.Rules;
using Platform.CQRS;

namespace Payments.ArchitectureTests.Application;

/// <summary>
/// Per architecture-tests.md § 1.4, every query is named <c>*Query</c>.
/// </summary>
public class QueryTests : BaseTest
{
    [Fact]
    public void Queries_Should_HaveNameEndingWith_Query()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .Should()
            .HaveNameEndingWith("Query")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Queries should follow the naming convention '*Query' for easy discovery and consistency");
    }
}
