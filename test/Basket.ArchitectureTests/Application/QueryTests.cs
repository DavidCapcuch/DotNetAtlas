using NetArchTest.Rules;
using Platform.CQRS;

namespace Basket.ArchitectureTests.Application;

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

    [Fact]
    public void Queries_Should_HaveCorrespondingHandler()
    {
        var queryTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .GetTypes()
            .Select(i => i.ReflectionType)
            .ToHashSet();

        var handlerTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .GetTypes()
            .ToHashSet();

        var handledQueryTypes = handlerTypes
            .SelectMany(h => h.ReflectionType.GetInterfaces())
            .Where(i => i.IsGenericType &&
                        i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var orphanQueries = queryTypes.Except(handledQueryTypes).ToList();

        orphanQueries.Should().BeEmpty(
            "Every query must have a corresponding handler. Orphan queries indicate dead code or " +
            "missing implementation. Found orphan queries: {0}",
            string.Join(", ", orphanQueries.Select(q => q.Name)));
    }
}
