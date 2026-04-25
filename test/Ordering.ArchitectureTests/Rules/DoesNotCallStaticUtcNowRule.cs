using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Ordering.ArchitectureTests.Rules;

/// <summary>
/// Walks every instruction in every method on the type and fails if it calls
/// <c>System.DateTime::get_UtcNow</c>, <c>System.DateTime::get_Now</c>,
/// <c>System.DateTimeOffset::get_UtcNow</c> or <c>System.DateTimeOffset::get_Now</c>.
/// Enforces ADR-0015 "inject TimeProvider; never read the clock statically"
/// inside <c>Ordering.Domain</c>.
/// </summary>
internal sealed class DoesNotCallStaticUtcNowRule : ICustomRule
{
    private static readonly string[] ForbiddenCallTargets =
    [
        "System.DateTime::get_UtcNow",
        "System.DateTime::get_Now",
        "System.DateTimeOffset::get_UtcNow",
        "System.DateTimeOffset::get_Now",
    ];

    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is not MethodReference target)
                {
                    continue;
                }

                var fullName = $"{target.DeclaringType.FullName}::{target.Name}";
                foreach (var forbidden in ForbiddenCallTargets)
                {
                    if (fullName == forbidden)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
