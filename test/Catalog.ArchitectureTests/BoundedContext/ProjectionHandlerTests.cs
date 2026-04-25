using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.ArchitectureTests.BoundedContext;

/// <summary>
/// Reconciles architecture-tests.md § 2.1 (which references a singular
/// <c>ProductSearchViewProjectionHandler</c>) with the sanctioned one-class-per-event design from
/// <c>catalog.md &lt;example_design_decision&gt;</c>: 8 sealed projection handlers, named
/// <c>*ProjectionHandler</c>, living under <c>Catalog.Application.{Aggregate}.{UseCase}</c>.
/// </summary>
public class ProjectionHandlerTests : BaseTest
{
    /// <summary>
    /// Every <see cref="IDomainEventHandler{T}"/> that is NOT an outbox publisher must end with
    /// <c>ProjectionHandler</c>. Selecting on the interface (not the suffix) is what gives the
    /// rule teeth — a future contributor naming a projection class <c>FooHandler</c> would fail
    /// here, where a tautological "*ProjectionHandler implies *ProjectionHandler" check would not.
    /// </summary>
    [Fact]
    public void DomainEventHandlers_ThatAreNotOutboxPublishers_Should_HaveNameEndingWith_ProjectionHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .And().DoNotHaveNameEndingWith("OutboxPublisher")
            .Should()
            .HaveNameEndingWith("ProjectionHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Every IDomainEventHandler<T> that isn't an OutboxPublisher must be a *ProjectionHandler — " +
            "locks Catalog's one-class-per-event read-side convention (catalog.md <example_design_decision>).");
    }

    [Fact]
    public void ProjectionHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("ProjectionHandler")
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Projection handlers should be sealed — each handles one event with one side effect");
    }

    /// <summary>
    /// Per the file-system convention established in M3, projection handlers live next to the
    /// command that triggered them: <c>Catalog.Application.Products.{UseCase}</c> or
    /// <c>Catalog.Application.Categories.{UseCase}</c>. The regex pins the two aggregate roots so
    /// a stray <c>Catalog.Application.Foo.ProjectionHandler</c> would fail the rule.
    /// </summary>
    [Fact]
    public void ProjectionHandlers_Should_LiveUnder_AggregateUseCaseNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("ProjectionHandler")
            .Should()
            .ResideInNamespaceMatching(@"^Catalog\.Application\.(Products|Categories)\.\w+$")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Projection handlers belong under 'Catalog.Application.<Aggregate>.<UseCase>' — keeps " +
            "the read-side build-up colocated with the write-side use case that triggered it");
    }
}
