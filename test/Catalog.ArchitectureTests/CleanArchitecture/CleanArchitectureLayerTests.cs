using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace Catalog.ArchitectureTests.CleanArchitecture;

/// <summary>
/// Per architecture-tests.md § 1.1, enforces the four-layer topology
/// (Domain ← Application ← Infrastructure ← Api) — no upward or sideways leaks.
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
    /// Per architecture-tests.md § 1.1 and CAT-ARCH-C03 (Wave-1 closeout), the Domain layer
    /// must not depend on infrastructure-side packages — EF Core, KafkaFlow, Redis, or the
    /// presentation-only FastEndpoints assemblies.
    /// </summary>
    [Fact]
    public void DomainLayer_ShouldNotHaveDependencyOn_InfrastructureTechnologies()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "KafkaFlow",
                "StackExchange.Redis",
                "FastEndpoints")
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    /// <summary>
    /// Per architecture-tests.md § 1.1 and CAT-ARCH-C03 (Wave-1 closeout), the Application
    /// layer must not depend on KafkaFlow, Redis, or FastEndpoints. EF Core is intentionally
    /// allowed here via the <c>ICatalogDbContext</c> port (the doc lists it as forbidden but
    /// the codebase's CQRS-on-EF-Core choice is documented in cross-cutting doc-reconcile
    /// issue D10 — kept Application-permitted until that reconciliation lands).
    /// </summary>
    [Fact]
    public void ApplicationLayer_ShouldNotHaveDependencyOn_InfrastructureTechnologies()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "KafkaFlow",
                "StackExchange.Redis",
                "FastEndpoints")
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    /// <summary>
    /// Per CAT-ARCH-C04 (Wave-1 closeout): aggregates must not raise external Avro events
    /// directly — the outbox publisher is responsible for translating internal domain events
    /// into <c>Platform.SchemaRegistry.Contracts</c> records.
    /// </summary>
    [Fact]
    public void DomainLayer_ShouldNotHaveDependencyOn_PlatformSchemaRegistryContracts()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny("Platform.SchemaRegistry.Contracts")
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }

    /// <summary>
    /// Per architecture-tests.md § 1.1 and CAT-ARCH-C01 (Wave-1 closeout), the Application
    /// layer must not reference presentation-only packages such as <c>FastEndpoints.*</c>.
    /// NetArchTest's IL-level dependency check cannot see unused <c>PackageReference</c>s,
    /// so this rule inspects the csproj XML directly.
    /// </summary>
    [Fact]
    public void ApplicationLayer_csproj_ShouldNotReference_FastEndpointsPackages()
    {
        var csprojPath = GetApplicationCsprojPath();
        var doc = XDocument.Load(csprojPath);

        var fastEndpointsPackages = doc
            .Descendants("PackageReference")
            .Select(p => p.Attribute("Include")?.Value)
            .Where(name => !string.IsNullOrEmpty(name) &&
                           name!.StartsWith("FastEndpoints.", StringComparison.Ordinal))
            .ToList();

        fastEndpointsPackages.Should().BeEmpty();
    }

    private static string GetApplicationCsprojPath([CallerFilePath] string thisFile = "")
    {
        // thisFile lives at <repo>/test/Catalog.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs
        var thisDir = new FileInfo(thisFile).Directory!;
        var repoRoot = thisDir.Parent!.Parent!.Parent!;
        return Path.Combine(repoRoot.FullName, "services", "Catalog", "Catalog.Application", "Catalog.Application.csproj");
    }
}
