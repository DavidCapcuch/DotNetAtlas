using NetArchTest.Rules;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.ArchitectureTests.BoundedContext;

/// <summary>
/// Catalog's read-side has two shapes: (a) 7 in-process per-event projections — each implementing
/// <see cref="IDomainEventHandler{T}"/>, suffix <c>*ProjectionDomainEventHandler</c>; (b) one
/// Kafka-delivered, inbox-deduped projection — implementing the custom Application port
/// <c>IStockLevelChangedProjector</c>, suffix <c>*ProjectionHandler</c> (the sole holdout of the
/// trigger-agnostic suffix, justified by its non-<c>IDomainEventHandler</c> contract). Both
/// shapes are sealed and live under <c>Catalog.Application.{Aggregate}.{UseCase}</c>. These
/// rules sharpen the universal U-D rule (architecture-tests.md § 1.3) for Catalog's specific
/// taxonomy.
/// </summary>
public class ProjectionHandlerTests : BaseTest
{
    /// <summary>
    /// Every <see cref="IDomainEventHandler{T}"/> that is NOT an outbox publisher must end with
    /// <c>ProjectionDomainEventHandler</c>. Selecting on the interface (not the suffix) gives
    /// the rule teeth — a future contributor naming a projection class
    /// <c>FooDomainEventHandler</c> (correct universal suffix but missing the role qualifier)
    /// would fail here, where a tautological "*ProjectionDomainEventHandler implies
    /// *ProjectionDomainEventHandler" check would not.
    /// </summary>
    [Fact]
    public void DomainEventHandlers_ThatAreNotOutboxPublishers_Should_HaveNameEndingWith_ProjectionDomainEventHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .And().DoNotHaveNameEndingWith("OutboxPublisherDomainEventHandler")
            .Should()
            .HaveNameEndingWith("ProjectionDomainEventHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Every IDomainEventHandler<T> that isn't an OutboxPublisherDomainEventHandler must " +
            "be a *ProjectionDomainEventHandler — locks Catalog's one-class-per-event read-side " +
            "convention.");
    }

    /// <summary>
    /// All <c>*ProjectionHandler</c>-family classes (both the U-D-suffixed
    /// <c>*ProjectionDomainEventHandler</c> and the sole Kafka-deduped <c>*ProjectionHandler</c>)
    /// must be sealed — each handles one shape of trigger with one side effect.
    /// </summary>
    [Fact]
    public void ProjectionHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("ProjectionHandler")
            .Or().HaveNameEndingWith("ProjectionDomainEventHandler")
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Projection handlers should be sealed — each handles one event with one side effect");
    }

    /// <summary>
    /// Per the file-system convention established, projection handlers live next to the
    /// command that triggered them: <c>Catalog.Application.Products.{UseCase}</c> or
    /// <c>Catalog.Application.Categories.{UseCase}</c>. The regex pins the two aggregate roots so
    /// a stray <c>Catalog.Application.Foo.ProjectionDomainEventHandler</c> would fail the rule.
    /// </summary>
    [Fact]
    public void ProjectionHandlers_Should_LiveUnder_AggregateUseCaseNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("ProjectionHandler")
            .Or().HaveNameEndingWith("ProjectionDomainEventHandler")
            .Should()
            .ResideInNamespaceMatching(@"^Catalog\.Application\.(Products|Categories)\.\w+$")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Projection handlers belong under 'Catalog.Application.<Aggregate>.<UseCase>' — keeps " +
            "the read-side build-up colocated with the write-side use case that triggered it");
    }
}
