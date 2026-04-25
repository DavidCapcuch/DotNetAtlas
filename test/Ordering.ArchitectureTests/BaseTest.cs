using System.Reflection;
using ApiAssemblyMarker = Ordering.API.IAssemblyMarker;
using ApplicationAssemblyMarker = Ordering.Application.IAssemblyMarker;
using DomainAssemblyMarker = Ordering.Domain.IAssemblyMarker;
using InfrastructureAssemblyMarker = Ordering.Infrastructure.IAssemblyMarker;

namespace Ordering.ArchitectureTests;

/// <summary>
/// Shared fixture exposing the four Ordering layer assemblies for NetArchTest
/// assertions. Mirrors <c>test/Weather.ArchitectureTests/BaseTest.cs</c>.
/// </summary>
public abstract class BaseTest
{
    protected static readonly Assembly DomainAssembly = typeof(DomainAssemblyMarker).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    protected static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;
    protected static readonly Assembly ApiAssembly = typeof(ApiAssemblyMarker).Assembly;
}
