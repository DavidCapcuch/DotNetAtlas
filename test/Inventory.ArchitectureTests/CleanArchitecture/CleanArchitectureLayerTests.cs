using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.CleanArchitecture;

/// <summary>
/// Enforces Inventory's four-layer topology (Domain ← Application ← Infrastructure ← Api) —
/// no upward or sideways leaks. Domain depends only on Platform.SharedKernel; Application
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
}
