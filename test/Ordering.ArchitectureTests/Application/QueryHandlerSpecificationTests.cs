using NetArchTest.Rules;

namespace Ordering.ArchitectureTests.Application;

/// <summary>
/// ADR-0021 / #277: CQRS query handlers must not depend on
/// <c>Ardalis.Specification</c>. Specs don't carry the SQL-side <c>Select</c> projection
/// that read paths need most, and sharing specs between read- and write-side handlers
/// couples the two models against the spirit of CQRS. The rule is scoped to
/// <c>*QueryHandler</c> types only — command handlers and infrastructure read-stores
/// remain free to use specs for write-side aggregate loading.
/// </summary>
public sealed class QueryHandlerSpecificationTests : BaseTest
{
    [Fact]
    public void QueryHandlers_ShouldNotHaveDependencyOn_ArdalisSpecification()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().HaveNameEndingWith("QueryHandler")
            .Should()
            .NotHaveDependencyOnAny("Ardalis.Specification")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0021, CQRS query handlers must not depend on Ardalis.Specification. " +
            "Specs don't carry SQL-side Select projection and sharing specs across read/write " +
            "couples the two models. Use inline LINQ in the handler.");
    }
}
