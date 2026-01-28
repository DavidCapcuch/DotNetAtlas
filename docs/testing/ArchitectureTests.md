<div align="center">

# 🏛️ Architecture Tests

</div>

| ⚡ TL;DR |
| -------- |
| Architecture tests use NetArchTest to enforce Clean Architecture rules at compile time. They verify layer dependencies, naming conventions, and structural patterns. Violations fail the build, preventing architectural drift. |

Architecture tests are automated guards that ensure your codebase follows architectural rules. They catch violations during CI/CD, not during code review.

## 🏗️ What We Test

```
┌─────────────────────────────────────────────────────────────┐
│                    Architecture Rules                        │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │              Layer Dependencies                         ││
│  │  Domain → (nothing)                                     ││
│  │  Application → Domain                                   ││
│  │  Infrastructure → Application, Domain                   ││
│  │  Api → Application, Domain, Infrastructure              ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │              Naming Conventions                         ││
│  │  *Handler → must implement ICommandHandler/IQueryHandler││
│  │  *Repository → must implement IRepository               ││
│  │  *Endpoint → must inherit from Endpoint                 ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │              Structural Rules                           ││
│  │  Domain entities → must be sealed                       ││
│  │  Value objects → must be immutable                      ││
│  │  Handlers → must be internal                            ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Setup

### Package Reference

```xml
<PackageReference Include="NetArchTest.Rules" Version="1.3.2" />
```

### Assembly References

```csharp
public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Feedback).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(SendFeedbackCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(WeatherDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
}
```

## 📏 Layer Dependency Tests

### Domain Has No Dependencies

```csharp
[Fact]
public void Domain_Should_Not_Have_Dependencies_On_Other_Layers()
{
    var result = Types.InAssembly(DomainAssembly)
        .ShouldNot()
        .HaveDependencyOnAny(
            "DotNetAtlas.Application",
            "DotNetAtlas.Infrastructure",
            "DotNetAtlas.Api")
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

### Application Depends Only On Domain

```csharp
[Fact]
public void Application_Should_Not_Depend_On_Infrastructure()
{
    var result = Types.InAssembly(ApplicationAssembly)
        .ShouldNot()
        .HaveDependencyOn("DotNetAtlas.Infrastructure")
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}

[Fact]
public void Application_Should_Not_Depend_On_Api()
{
    var result = Types.InAssembly(ApplicationAssembly)
        .ShouldNot()
        .HaveDependencyOn("DotNetAtlas.Api")
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

### Infrastructure Can Depend On Application And Domain

```csharp
[Fact]
public void Infrastructure_Should_Not_Depend_On_Api()
{
    var result = Types.InAssembly(InfrastructureAssembly)
        .ShouldNot()
        .HaveDependencyOn("DotNetAtlas.Api")
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

## 📛 Naming Convention Tests

### Handlers Must Implement Interface

```csharp
[Fact]
public void Handlers_Should_Implement_Handler_Interface()
{
    var result = Types.InAssembly(ApplicationAssembly)
        .That()
        .HaveNameEndingWith("Handler")
        .Should()
        .ImplementInterface(typeof(ICommandHandler<,>))
        .Or()
        .ImplementInterface(typeof(IQueryHandler<,>))
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

### Repositories Must Implement Interface

```csharp
[Fact]
public void Repositories_Should_Implement_Repository_Interface()
{
    var result = Types.InAssembly(InfrastructureAssembly)
        .That()
        .HaveNameEndingWith("Repository")
        .And()
        .AreClasses()
        .Should()
        .ImplementInterface(typeof(IRepository<>))
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

### Endpoints Must Inherit From Base

```csharp
[Fact]
public void Endpoints_Should_Inherit_From_Endpoint()
{
    var result = Types.InAssembly(ApiAssembly)
        .That()
        .HaveNameEndingWith("Endpoint")
        .Should()
        .Inherit(typeof(Endpoint<,>))
        .Or()
        .Inherit(typeof(EndpointWithoutRequest<>))
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

## 🔒 Structural Tests

### Domain Entities Must Be Sealed

```csharp
[Fact]
public void Domain_Entities_Should_Be_Sealed()
{
    var result = Types.InAssembly(DomainAssembly)
        .That()
        .Inherit(typeof(Entity<>))
        .Should()
        .BeSealed()
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

### Handlers Should Be Internal

```csharp
[Fact]
public void Handlers_Should_Be_Internal()
{
    var result = Types.InAssembly(ApplicationAssembly)
        .That()
        .HaveNameEndingWith("Handler")
        .Should()
        .NotBePublic()
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

### Commands And Queries Should Be Records

```csharp
[Fact]
public void Commands_Should_Be_Records()
{
    var result = Types.InAssembly(ApplicationAssembly)
        .That()
        .ImplementInterface(typeof(ICommand<>))
        .Should()
        .BeRecord()
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue(
        because: FormatFailingTypes(result));
}
```

## 🔗 Dependency Direction Tests

```csharp
[Fact]
public void All_Layers_Should_Follow_Dependency_Rule()
{
    // Domain: no dependencies
    // Application: only Domain
    // Infrastructure: Application + Domain
    // Api: all layers
    
    var domainTypes = Types.InAssembly(DomainAssembly);
    var applicationTypes = Types.InAssembly(ApplicationAssembly);
    var infrastructureTypes = Types.InAssembly(InfrastructureAssembly);
    
    // Verify dependency direction
    domainTypes.ShouldNot()
        .HaveDependencyOnAny("Application", "Infrastructure", "Api")
        .GetResult()
        .IsSuccessful.Should().BeTrue();
    
    applicationTypes.ShouldNot()
        .HaveDependencyOnAny("Infrastructure", "Api")
        .GetResult()
        .IsSuccessful.Should().BeTrue();
    
    infrastructureTypes.ShouldNot()
        .HaveDependencyOn("Api")
        .GetResult()
        .IsSuccessful.Should().BeTrue();
}
```

## 🛠️ Helper Methods

```csharp
private static string FormatFailingTypes(TestResult result)
{
    if (result.IsSuccessful)
        return string.Empty;
    
    var failingTypes = result.FailingTypes?
        .Select(t => t.FullName)
        .ToList() ?? [];
    
    return $"Failing types: {string.Join(", ", failingTypes)}";
}
```

## 🏃 Running Tests

```bash
# Run architecture tests
dotnet test tests/DotNetAtlas.ArchitectureTests

# Run as part of CI
dotnet test --filter "Category=Architecture"
```

## 📖 Further Reading

- [**Clean Architecture**](../architecture/CleanArchitecture.md) - The rules we enforce
- [**Testing Overview**](Overview.md) - Testing strategy
- [NetArchTest Documentation](https://github.com/BenMorris/NetArchTest)

