using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EShop.BFF.ArchitectureTests;

/// <summary>
/// Shared assembly anchors + custom IL-scan rules for the BFF architecture tests. The BFF is a
/// 2-layer aggregation gateway (bff.md § 1), so there are only Api and Infrastructure assemblies.
/// </summary>
public abstract class BaseTest
{
    protected static readonly Assembly ApiAssembly = typeof(EShop.BFF.Api.IAssemblyMarker).Assembly;

    protected static readonly Assembly InfrastructureAssembly = typeof(EShop.BFF.Infrastructure.IAssemblyMarker).Assembly;

    /// <summary>Fails a type that loads a string literal containing <paramref name="fragment"/>
    /// anywhere in its IL (including nested closures).</summary>
    protected sealed class DoesNotLoadStringContainingRule(string fragment) : NetArchTest.Rules.ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethods(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string value
                        && value.Contains(fragment, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>Passes only when the type loads the exact string <paramref name="required"/>
    /// somewhere in its IL.</summary>
    protected sealed class LoadsStringRule(string required) : NetArchTest.Rules.ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethods(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string value
                        && string.Equals(value, required, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private static IEnumerable<MethodDefinition> AllMethods(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            yield return method;
        }

        foreach (var nested in type.NestedTypes)
        {
            foreach (var method in AllMethods(nested))
            {
                yield return method;
            }
        }
    }
}
