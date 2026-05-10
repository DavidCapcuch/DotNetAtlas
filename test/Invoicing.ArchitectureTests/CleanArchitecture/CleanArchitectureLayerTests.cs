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
}
