using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.CleanArchitecture;

public sealed class CleanArchitectureLayerTests : BaseTest
{
    [Fact]
    public void Domain_ShouldNotHaveDependencyOnAny_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApplicationAssembly.GetName().Name)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void Domain_ShouldNotHaveDependencyOnAny_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureAssembly.GetName().Name)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void Domain_ShouldNotHaveDependencyOnAny_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApiAssembly.GetName().Name)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOnAny_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureAssembly.GetName().Name)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOnAny_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApiAssembly.GetName().Name)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void Infrastructure_ShouldNotHaveDependencyOnAny_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApiAssembly.GetName().Name)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    /// <summary>
    /// The package half of the § 1.1 dependency table. The sibling facts pass sibling assembly
    /// names, which NetArchTest matches as <em>namespace</em> prefixes — so they catch only types
    /// under <c>{Bc}.Application.*</c> / <c>.Infrastructure.*</c> / <c>.Api.*</c>, never a domain
    /// type taking <c>DbContext</c>, <c>IDatabase</c>, or a KafkaFlow/FastEndpoints type from a
    /// NuGet package.
    /// </summary>
    [Fact]
    public void Domain_ShouldNotHaveDependencyOnAny_InfrastructurePackages()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Ardalis.Specification.EntityFrameworkCore",
                "KafkaFlow",
                "FastEndpoints",
                "StackExchange.Redis")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Invoicing.Domain must not reference EF Core, KafkaFlow, FastEndpoints or Redis " +
            "(architecture-tests.md § 1.1): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    /// <summary>
    /// The package half of the § 1.1 Application row. EF Core is deliberately absent from the
    /// forbidden set: <c>IInvoicingDbContext</c> inherits <c>IOutboxDbContext</c>, which exposes
    /// <c>DbSet&lt;OutboxMessage&gt;</c> and <c>DatabaseFacade</c> — so Application references EF
    /// Core by design, and only the concrete DbContext belongs to Infrastructure.
    /// </summary>
    [Fact]
    public void Application_ShouldNotHaveDependencyOnAny_InfrastructurePackages()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "KafkaFlow",
                "FastEndpoints",
                "StackExchange.Redis")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Invoicing.Application must not reference KafkaFlow, FastEndpoints or Redis — those " +
            "are Infrastructure/Api concerns (architecture-tests.md § 1.1): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }
}
