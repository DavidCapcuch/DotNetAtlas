using Mono.Cecil;
using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.Rules;

/// <summary>
/// Asserts a type exposes at least one <c>public static</c> method whose name
/// starts with <c>Create</c> or <c>From</c> (architecture-tests.md § 1.2).
/// Mirrors <c>Ordering.ArchitectureTests.Rules.HasPublicStaticFactoryMethodRule</c>.
/// </summary>
internal sealed class HasPublicStaticFactoryMethodRule : ICustomRule
{
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.IsPublic && method.IsStatic
                && (method.Name.StartsWith("Create", StringComparison.Ordinal)
                    || method.Name.StartsWith("From", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
