using System.Reflection;
using Mono.Cecil;
using ApiAssemblyMarker = Invoicing.API.IAssemblyMarker;
using ApplicationAssemblyMarker = Invoicing.Application.IAssemblyMarker;
using DomainAssemblyMarker = Invoicing.Domain.IAssemblyMarker;
using InfrastructureAssemblyMarker = Invoicing.Infrastructure.IAssemblyMarker;

namespace Invoicing.ArchitectureTests;

/// <summary>
/// Shared fixture exposing the four Invoicing layer assemblies for NetArchTest
/// assertions. Mirrors <c>test/Ordering.ArchitectureTests/BaseTest.cs</c>.
/// </summary>
public abstract class BaseTest
{
    protected static readonly Assembly DomainAssembly = typeof(DomainAssemblyMarker).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    protected static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;
    protected static readonly Assembly ApiAssembly = typeof(ApiAssemblyMarker).Assembly;

    /// <summary>
    /// Walks the type and every nested compiler-generated type. Required so IL-scanning
    /// rules see async state machines (<c>&lt;HandleAsync&gt;d__N::MoveNext</c>), iterator
    /// state machines, and lambda closures (<c>&lt;&gt;c__DisplayClass*</c>). Without this,
    /// async methods' IL is invisible — and every M7 command handler is async.
    /// Mirrors <c>Catalog.ArchitectureTests.BaseTest.AllMethodsIncludingNested</c>.
    /// </summary>
    internal static IEnumerable<MethodDefinition> AllMethodsIncludingNested(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            yield return method;
        }

        foreach (var nested in type.NestedTypes)
        {
            foreach (var method in AllMethodsIncludingNested(nested))
            {
                yield return method;
            }
        }
    }
}
