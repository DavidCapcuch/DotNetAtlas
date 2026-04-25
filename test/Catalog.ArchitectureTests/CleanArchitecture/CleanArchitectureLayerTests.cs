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
}
