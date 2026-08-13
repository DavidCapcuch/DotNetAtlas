using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.CleanArchitecture;

/// <summary>
/// Enforces Inventory's four-layer topology (Domain ← Application ← Infrastructure ← Api) —
/// no upward or sideways leaks. Domain is the only layer with no infrastructure SDK; Application
/// depends on Domain + Platform.CQRS; Infrastructure depends on Application; Api wires them
/// all together.
/// </summary>
public class CleanArchitectureLayerTests : BaseTest
{
    [Fact]
    public void DomainLayer_ShouldNotHaveDependencyOnAny_ApplicationLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApplicationAssembly.GetName().Name)
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void DomainLayer_ShouldNotHaveDependencyOnAny_InfrastructureLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureAssembly.GetName().Name)
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void DomainLayer_ShouldNotHaveDependencyOnAny_PresentationLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(PresentationAssembly.GetName().Name)
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void ApplicationLayer_ShouldNotHaveDependencyOnAny_InfrastructureLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureAssembly.GetName().Name)
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void ApplicationLayer_ShouldNotHaveDependencyOnAny_PresentationLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(PresentationAssembly.GetName().Name)
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void InfrastructureLayer_ShouldNotHaveDependencyOnAny_PresentationLayer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(PresentationAssembly.GetName().Name)
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
            "Inventory.Domain must not reference EF Core, KafkaFlow, FastEndpoints or Redis " +
            "(architecture-tests.md § 1.1): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    /// <summary>
    /// The package half of the § 1.1 Application row. EF Core is deliberately absent from the
    /// forbidden set: <c>IInventoryDbContext</c> inherits <c>IOutboxDbContext</c>, which exposes
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
            "Inventory.Application must not reference KafkaFlow, FastEndpoints or Redis — those " +
            "are Infrastructure/Api concerns (architecture-tests.md § 1.1): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }
}
