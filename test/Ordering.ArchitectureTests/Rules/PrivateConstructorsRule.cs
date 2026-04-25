using Mono.Cecil;
using Mono.Cecil.Rocks;
using NetArchTest.Rules;

namespace Ordering.ArchitectureTests.Rules;

/// <summary>
/// Asserts that every instance constructor declared on a type is private.
/// Used to enforce factory-method-only construction on aggregate roots
/// (architecture-tests.md § 1.2).
/// </summary>
internal sealed class PrivateConstructorsRule : ICustomRule
{
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var constructor in type.GetConstructors())
        {
            if (!constructor.IsStatic && !constructor.IsPrivate)
            {
                return false;
            }
        }

        return true;
    }
}
